using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using Steervo.Application.Accounts;
using Steervo.Application.Documents;
using Steervo.Application.Evaluations;
using Steervo.Infrastructure.Data;
using Steervo.Infrastructure.Documents;
using Steervo.Infrastructure.Identity;
using Steervo.Infrastructure.Mail;
using Steervo.Web;

// Command-line switches that run one maintenance task and exit instead of serving requests.
const string MigrateSwitch = "--migrate";
const string CleanupDocumentsSwitch = "--cleanup-documents";

const string DefaultPublicUrl = "http://localhost:5180";
const string HealthPath = "/health";
const string ErrorPath = "/Error";
const string AuthenticationPolicy = "authentication";
const string FileSystemProvider = "FileSystem";
const string S3Provider = "S3";

// Requests per client address and minute.
const int GeneralRequestsPerMinute = 120;
const int AuthenticationRequestsPerMinute = 20;

// Every regular start removes a small batch of due documents; the cleanup switch removes all.
const int StartupCleanupBatch = 20;

var builder = WebApplication.CreateBuilder(args);

AddPersistence(builder);
AddPages(builder);
AddSignIn(builder);

var publicUrl = new Uri(builder.Configuration["PublicUrl"] ?? DefaultPublicUrl);

if (!builder.Environment.IsDevelopment() && publicUrl.Scheme != "https")
{
    throw new InvalidOperationException("PublicUrl must use HTTPS in production.");
}

AddPasskeys(builder, publicUrl);
AddForwardedHeaders(builder);
AddApplicationServices(builder);
AddDocumentStorage(builder);
AddRateLimits(builder);

var app = builder.Build();

if (args.Contains(MigrateSwitch))
{
    await MigrateDatabaseAsync(app);
    return;
}

var cleanupRequested = args.Contains(CleanupDocumentsSwitch);
await RemoveDueDocumentsAsync(app, cleanupRequested);

if (cleanupRequested)
{
    return;
}

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler(ErrorPath);
    app.UseHsts();

    // Health probes are answered directly and never redirected.
    app.UseWhen(
        context => !context.Request.Path.StartsWithSegments(HealthPath),
        branch => branch.UseHttpsRedirection()
    );
}

app.Use(
    async (context, next) =>
    {
        ApplySecurityHeaders(context.Response.Headers);
        await next();
    }
);

// Texts are German; numbers and dates follow the German-language region the browser asks for.
app.UseRequestLocalization(GermanLocalization());
app.UseStaticFiles();

// Full browser pages get a useful error view; fragments and form responses keep their status and body.
app.UseWhen(
    IsBrowserPageRequest,
    branch => branch.UseStatusCodePagesWithReExecute(ErrorPath, "?statusCode={0}")
);

app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health/live", () => Results.Ok()).AllowAnonymous();
app.MapGet("/health/ready", async (AppDbContext db) => await CheckReadinessAsync(db))
    .AllowAnonymous();
app.MapRazorPages();

await app.RunAsync();

static void AddPersistence(WebApplicationBuilder builder)
{
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseNpgsql(
            builder.Configuration.GetConnectionString("Database")
                ?? throw new InvalidOperationException("ConnectionStrings:Database is required.")
        )
    );

    builder
        .Services.AddDataProtection()
        .SetApplicationName("Steervo.Community")
        .PersistKeysToFileSystem(
            new DirectoryInfo(builder.Configuration["DataProtection:Path"] ?? ".data/keys")
        );
}

static void AddPages(WebApplicationBuilder builder)
{
    builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");

    builder
        .Services.AddRazorPages(options =>
        {
            // Everything requires a signed-in user except the sign-in pages and the error page.
            options.Conventions.AuthorizeFolder("/");
            options.Conventions.AllowAnonymousToFolder("/Auth");
            options.Conventions.AllowAnonymousToPage(ErrorPath);
        })
        .AddViewLocalization()
        .AddMvcOptions(options =>
            options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true
        );

    builder.Services.AddHttpContextAccessor();
}

static void AddSignIn(WebApplicationBuilder builder)
{
    builder
        .Services.AddIdentityCore<AppUser>(options =>
        {
            options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
            options.User.RequireUniqueEmail = true;
            options.SignIn.RequireConfirmedEmail = true;
        })
        .AddEntityFrameworkStores<AppDbContext>()
        .AddSignInManager();

    builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();

    builder.Services.ConfigureApplicationCookie(options =>
    {
        options.LoginPath = "/Auth/Login";
        options.AccessDeniedPath = ErrorPath;
        options.Cookie.Name = "Steervo.Session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;

        // A session ends eight hours after sign-in, whether or not it was used in between.
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = false;
    });
}

static void AddPasskeys(WebApplicationBuilder builder, Uri publicUrl)
{
    builder.Services.Configure<IdentityPasskeyOptions>(options =>
    {
        options.ServerDomain = publicUrl.Host;
        options.UserVerificationRequirement = "required";
        options.ResidentKeyRequirement = "required";

        // Passkeys are only accepted from this installation's own public address.
        options.ValidateOrigin = context =>
        {
            var isOwnOrigin =
                !context.CrossOrigin
                && context.Origin == publicUrl.GetLeftPart(UriPartial.Authority);

            return ValueTask.FromResult(isOwnOrigin);
        };
    });
}

static void AddForwardedHeaders(WebApplicationBuilder builder)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders =
            ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

        var trustedProxies =
            builder.Configuration.GetSection("TrustedProxies").Get<string[]>() ?? [];

        foreach (var address in trustedProxies)
        {
            options.KnownProxies.Add(IPAddress.Parse(address));
        }
    });
}

static void AddApplicationServices(WebApplicationBuilder builder)
{
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
}

static void AddDocumentStorage(WebApplicationBuilder builder)
{
    builder.Services.AddScoped<DocumentWorkspace>();
    builder.Services.AddScoped<IDocumentStorage, DocumentStorage>();
    builder.Services.AddScoped<DocumentCleanup>();
    builder.Services.Configure<DocumentStorageOptions>(
        builder.Configuration.GetSection("DocumentStorage")
    );

    // Without an explicit choice, development stores documents on disk and production in S3.
    var defaultProvider = builder.Environment.IsDevelopment() ? FileSystemProvider : S3Provider;
    var provider = builder.Configuration["DocumentStorage:Provider"] ?? defaultProvider;

    if (provider == FileSystemProvider)
    {
        builder.Services.AddSingleton<IDocumentObjects, FileSystemDocumentObjects>();
    }
    else if (provider == S3Provider)
    {
        builder.Services.AddSingleton<IDocumentObjects, S3DocumentObjects>();
    }
    else
    {
        throw new InvalidOperationException("DocumentStorage:Provider must be FileSystem or S3.");
    }
}

static void AddRateLimits(WebApplicationBuilder builder)
{
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = 429;

        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            // The re-executed error page must render even for a client that is being throttled.
            var isErrorPage = context.Features.Get<IStatusCodeReExecuteFeature>() is not null;

            return isErrorPage
                ? RateLimitPartition.GetNoLimiter("status-page")
                : LimitPerClient(context, GeneralRequestsPerMinute);
        });

        options.AddPolicy(
            AuthenticationPolicy,
            context => LimitPerClient(context, AuthenticationRequestsPerMinute)
        );
    });
}

static RateLimitPartition<string> LimitPerClient(HttpContext context, int requestsPerMinute)
{
    var clientAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    return RateLimitPartition.GetFixedWindowLimiter(
        clientAddress,
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = requestsPerMinute,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }
    );
}

static async Task MigrateDatabaseAsync(WebApplication app)
{
    await using var scope = app.Services.CreateAsyncScope();
    var database = scope.ServiceProvider.GetRequiredService<AppDbContext>().Database;

    await database.MigrateAsync();
}

static async Task RemoveDueDocumentsAsync(WebApplication app, bool cleanupRequested)
{
    await using var scope = app.Services.CreateAsyncScope();

    var cleanup = scope.ServiceProvider.GetRequiredService<DocumentCleanup>();
    var batchSize = cleanupRequested ? int.MaxValue : StartupCleanupBatch;
    var removed = await cleanup.RunAsync(batchSize);

    if (!cleanupRequested)
    {
        return;
    }

    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var remaining = await db.Set<PendingDocumentDeletion>()
        .CountAsync(deletion => deletion.AvailableAt <= DateTimeOffset.UtcNow);

    app.Logger.LogInformation(
        "Document cleanup processed {Removed} objects; {Remaining} due deletions remain.",
        removed,
        remaining
    );

    // A non-zero exit code reports that due deletions are still waiting.
    if (remaining > 0)
    {
        Environment.ExitCode = 1;
    }
}

static void ApplySecurityHeaders(IHeaderDictionary headers)
{
    headers.ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    headers.XContentTypeOptions = "nosniff";
    headers["Referrer-Policy"] = "same-origin";
    headers.CacheControl = "no-cache, no-store";
    headers.Pragma = "no-cache";
}

static RequestLocalizationOptions GermanLocalization()
{
    string[] regions = ["de", "de-DE", "de-CH", "de-AT", "de-LI", "de-LU"];

    return new RequestLocalizationOptions
    {
        DefaultRequestCulture = new RequestCulture("de"),
        SupportedUICultures = [new CultureInfo("de")],
        SupportedCultures = [.. regions.Select(name => new CultureInfo(name))],
    };
}

static bool IsBrowserPageRequest(HttpContext context)
{
    var request = context.Request;

    if (!HttpMethods.IsGet(request.Method))
    {
        return false;
    }

    if (request.Headers.ContainsKey("HX-Request"))
    {
        return false;
    }

    if (request.Path.StartsWithSegments(HealthPath))
    {
        return false;
    }

    return request.Headers.Accept.Any(value =>
        value?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true
    );
}

static async Task<IResult> CheckReadinessAsync(AppDbContext db)
{
    try
    {
        if (!await db.Database.CanConnectAsync())
        {
            return Results.StatusCode(503);
        }

        var pendingMigrations = await db.Database.GetPendingMigrationsAsync();

        return pendingMigrations.Any() ? Results.StatusCode(503) : Results.Ok();
    }
    catch (Npgsql.NpgsqlException)
    {
        return Results.StatusCode(503);
    }
}

public partial class Program;
