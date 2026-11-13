using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using Steervo.Application.Accounts;
using Steervo.Application.Evaluations;
using Steervo.Application.Documents;
using Steervo.Infrastructure.Documents;
using Steervo.Infrastructure.Data;
using Steervo.Infrastructure.Identity;
using Steervo.Infrastructure.Mail;
using Steervo.Web;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(builder.Configuration.GetConnectionString("Database")
    ?? throw new InvalidOperationException("ConnectionStrings:Database is required.")));
builder.Services.AddDataProtection().SetApplicationName("Steervo.Community")
    .PersistKeysToFileSystem(new DirectoryInfo(builder.Configuration["DataProtection:Path"] ?? ".data/keys"));
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
builder.Services.AddRazorPages(options => {
    options.Conventions.AuthorizeFolder("/");
    options.Conventions.AllowAnonymousToFolder("/Auth");
    options.Conventions.AllowAnonymousToPage("/Error");
}).AddViewLocalization().AddMvcOptions(options => options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true);
builder.Services.AddHttpContextAccessor();
builder.Services.AddIdentityCore<AppUser>(options => { options.Stores.SchemaVersion = IdentitySchemaVersions.Version3; options.User.RequireUniqueEmail = true; options.SignIn.RequireConfirmedEmail = true; })
    .AddEntityFrameworkStores<AppDbContext>().AddSignInManager();
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
builder.Services.ConfigureApplicationCookie(options => {
    options.LoginPath = "/Auth/Login"; options.AccessDeniedPath = "/Error";
    options.Cookie.Name = "Steervo.Session"; options.Cookie.HttpOnly = true; options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromHours(8); options.SlidingExpiration = false;
});
var publicUrl = new Uri(builder.Configuration["PublicUrl"] ?? "http://localhost:5180");
if (!builder.Environment.IsDevelopment() && publicUrl.Scheme != "https") throw new InvalidOperationException("PublicUrl must use HTTPS in production.");
builder.Services.Configure<IdentityPasskeyOptions>(options => {
    options.ServerDomain = publicUrl.Host;
    options.UserVerificationRequirement = "required";
    options.ResidentKeyRequirement = "required";
    options.ValidateOrigin = context => ValueTask.FromResult(!context.CrossOrigin && context.Origin == publicUrl.GetLeftPart(UriPartial.Authority));
});
builder.Services.Configure<ForwardedHeadersOptions>(options => {
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    foreach (var address in builder.Configuration.GetSection("TrustedProxies").Get<string[]>() ?? []) options.KnownProxies.Add(IPAddress.Parse(address));
});
builder.Services.Configure<SetupOptions>(builder.Configuration.GetSection("Setup"));
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection("Smtp"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IAccountAccess, AccountAccess>();
builder.Services.AddScoped<IAccountMail, LocalizedAccountMail>();
builder.Services.AddScoped<SmtpDelivery>();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<IAccountDirectory, AccountDirectory>();
builder.Services.AddScoped<IEvaluationStore, EvaluationStore>();
builder.Services.AddScoped<EvaluationWorkspace>();
builder.Services.AddScoped<CandidateWorkspace>();
builder.Services.AddScoped<Steervo.Application.Costs.CostWorkspace>();
builder.Services.AddScoped<Steervo.Application.Scenarios.ScenarioWorkspace>();
builder.Services.AddScoped<Steervo.Application.Offers.OfferWorkspace>();
builder.Services.AddScoped<EvaluationTransfer>();
builder.Services.AddScoped<EvaluationArchive>();
builder.Services.AddScoped<DocumentWorkspace>();
builder.Services.AddScoped<IDocumentStorage, DocumentStorage>();
builder.Services.AddScoped<DocumentCleanup>();
builder.Services.Configure<DocumentStorageOptions>(builder.Configuration.GetSection("DocumentStorage"));
var documentProvider = builder.Configuration["DocumentStorage:Provider"] ?? (builder.Environment.IsDevelopment() ? "FileSystem" : "S3");
if (documentProvider == "FileSystem") builder.Services.AddSingleton<IDocumentObjects, FileSystemDocumentObjects>();
else if (documentProvider == "S3") builder.Services.AddSingleton<IDocumentObjects, S3DocumentObjects>();
else throw new InvalidOperationException("DocumentStorage:Provider must be FileSystem or S3.");
builder.Services.AddRateLimiter(options => {
    options.RejectionStatusCode = 429;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        context.Features.Get<IStatusCodeReExecuteFeature>() is not null
            ? RateLimitPartition.GetNoLimiter("status-page")
            : RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("authentication", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

var app = builder.Build();
if (args.Contains("--migrate"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    return;
}
await using (var scope = app.Services.CreateAsyncScope())
{
    var cleanup = scope.ServiceProvider.GetRequiredService<DocumentCleanup>();
    var removed = await cleanup.RunAsync(args.Contains("--cleanup-documents") ? int.MaxValue : 20);
    if (args.Contains("--cleanup-documents"))
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var remaining = await db.Set<PendingDocumentDeletion>().CountAsync(p => p.AvailableAt <= DateTimeOffset.UtcNow);
        app.Logger.LogInformation("Document cleanup processed {Removed} objects; {Remaining} due deletions remain.", removed, remaining);
        if (remaining > 0) Environment.ExitCode = 1;
    }
}
if (args.Contains("--cleanup-documents")) return;
app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment()) { app.UseExceptionHandler("/Error"); app.UseHsts(); app.UseWhen(context => !context.Request.Path.StartsWithSegments("/health"), branch => branch.UseHttpsRedirection()); }
app.Use(async (context, next) => {
    context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "same-origin";
    context.Response.Headers.CacheControl = "no-cache, no-store";
    context.Response.Headers.Pragma = "no-cache";
    await next();
});
app.UseRequestLocalization(new RequestLocalizationOptions {
    DefaultRequestCulture = new RequestCulture("de"), SupportedCultures = [new CultureInfo("de")], SupportedUICultures = [new CultureInfo("de")]
});
app.UseStaticFiles();
// Full browser pages get a useful error view; fragments and form responses keep their status and body.
app.UseWhen(context => HttpMethods.IsGet(context.Request.Method)
    && !context.Request.Headers.ContainsKey("HX-Request")
    && !context.Request.Path.StartsWithSegments("/health")
    && context.Request.Headers.Accept.Any(value => value?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true),
    branch => branch.UseStatusCodePagesWithReExecute("/Error", "?statusCode={0}"));
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication(); app.UseAuthorization();
app.MapGet("/health/live", () => Results.Ok()).AllowAnonymous();
app.MapGet("/health/ready", async (AppDbContext db) =>
{
    try { return await db.Database.CanConnectAsync() && !(await db.Database.GetPendingMigrationsAsync()).Any() ? Results.Ok() : Results.StatusCode(503); }
    catch (Npgsql.NpgsqlException) { return Results.StatusCode(503); }
}).AllowAnonymous();
app.MapRazorPages();
await app.RunAsync();

public partial class Program;
