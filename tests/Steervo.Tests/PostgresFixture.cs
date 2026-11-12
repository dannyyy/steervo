using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Steervo.Application.Accounts;
using Steervo.Application.Documents;
using Steervo.Infrastructure.Documents;
using Microsoft.Extensions.Options;
using Steervo.Application.Evaluations;
using Steervo.Infrastructure.Data;
using Steervo.Infrastructure.Identity;

namespace Steervo.Tests;

public sealed class TestClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => Now;
}
public sealed class TestMail : IAccountMail
{
    public Dictionary<string, string> Codes { get; } = [];
    public Task SendCodeAsync(string email, string code) { Codes[email] = code; return Task.CompletedTask; }
    public Task SendInvitationAsync(string email) => Task.CompletedTask;
}
public sealed record TestActor(string Id) : ICurrentUser;

public sealed class PostgresFixture : IAsyncDisposable
{
    public const string SetupToken = "test-only-setup-token-with-at-least-32-characters";
    public ServiceProvider Services { get; private set; } = null!;
    public TestClock Clock { get; } = new();
    public TestMail Mail { get; } = new();
    private readonly string documentPath = Path.Combine(Path.GetTempPath(), "steervo-test-documents-" + Guid.NewGuid().ToString("N"));
    private string adminConnection = "";
    private readonly string databaseName = "steervo_test_" + Guid.NewGuid().ToString("N");
    public static async Task<PostgresFixture> CreateAsync(int port, bool s3 = false)
    {
        var fixture = new PostgresFixture();
        var configured = Environment.GetEnvironmentVariable(port == 55437 ? "STEERVO_TEST_POSTGRES17" : "STEERVO_TEST_POSTGRES18");
        fixture.adminConnection = configured ?? $"Host=localhost;Port={port};Database=steervo;Username=steervo;Password=steervo-local-only";
        await using var connection = new NpgsqlConnection(fixture.adminConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE {fixture.databaseName}", connection);
        await command.ExecuteNonQueryAsync();
        var databaseConnection = new NpgsqlConnectionStringBuilder(fixture.adminConnection) { Database = fixture.databaseName }.ConnectionString;
        var services = new ServiceCollection();
        services.AddLogging(); services.AddDataProtection().UseEphemeralDataProtectionProvider();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(databaseConnection));
        services.AddIdentityCore<AppUser>(o => { o.Stores.SchemaVersion = IdentitySchemaVersions.Version3; o.User.RequireUniqueEmail = true; }).AddEntityFrameworkStores<AppDbContext>();
        services.Configure<SetupOptions>(o => o.Token = SetupToken);
        services.AddSingleton<TimeProvider>(fixture.Clock); services.AddSingleton<IAccountMail>(fixture.Mail);
        services.AddScoped<IAccountAccess, AccountAccess>();
        services.AddScoped<IEvaluationStore, EvaluationStore>(); services.AddScoped<IAccountDirectory, AccountDirectory>();
        services.AddSingleton<IDocumentObjects>(s3
            ? new S3DocumentObjects(Options.Create(new DocumentStorageOptions { Endpoint = "http://localhost:55439", Bucket = "steervo-tests", Region = "us-east-1", AccessKey = "test", SecretKey = "test" }))
            : new FileSystemDocumentObjects(Options.Create(new DocumentStorageOptions { Path = fixture.documentPath })));
        services.AddScoped<IDocumentStorage, DocumentStorage>(); services.AddScoped<DocumentCleanup>();
        fixture.Services = services.BuildServiceProvider();
        try { await fixture.RunAsync(async provider => { await provider.GetRequiredService<AppDbContext>().Database.MigrateAsync(); return true; }); }
        catch { await fixture.DisposeAsync(); throw; }
        return fixture;
    }
    public async Task<T> RunAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }
    public Task<T> AccountAsync<T>(Func<IAccountAccess, Task<T>> action) => RunAsync(p => action(p.GetRequiredService<IAccountAccess>()));
    public Task<T> AsAsync<T>(string actor, Func<EvaluationWorkspace, Task<T>> action) => RunAsync(p => action(new EvaluationWorkspace(p.GetRequiredService<IEvaluationStore>(), new TestActor(actor), p.GetRequiredService<IAccountDirectory>())));
    public Task<T> TransferAsync<T>(string actor, Func<EvaluationTransfer, Task<T>> action) => RunAsync(p => {
        var store = p.GetRequiredService<IEvaluationStore>(); var current = new TestActor(actor);
        return action(new EvaluationTransfer(new EvaluationWorkspace(store, current, p.GetRequiredService<IAccountDirectory>()), store, current));
    });
    public Task<T> DocumentsAsync<T>(string actor, Func<DocumentWorkspace, Task<T>> action) => RunAsync(p => {
        var current = new TestActor(actor); var store = p.GetRequiredService<IEvaluationStore>();
        return action(new DocumentWorkspace(new EvaluationWorkspace(store, current, p.GetRequiredService<IAccountDirectory>()), current, p.GetRequiredService<IDocumentStorage>()));
    });
    public Task<T> ArchiveAsync<T>(string actor, Func<EvaluationArchive, Task<T>> action) => RunAsync(p => {
        var current = new TestActor(actor); var store = p.GetRequiredService<IEvaluationStore>();
        var workspace = new EvaluationWorkspace(store, current, p.GetRequiredService<IAccountDirectory>());
        return action(new EvaluationArchive(workspace, new EvaluationTransfer(workspace, store, current), store, p.GetRequiredService<IDocumentStorage>()));
    });
    public async Task<string> SetupAsync(string email = "owner@example.test")
    {
        var id = await AccountAsync(a => a.BeginSetupAsync(SetupToken, email, "Owner"));
        return await AccountAsync(a => a.VerifyCodeAsync(id, Mail.Codes[email.ToUpperInvariant()]));
    }
    public async Task<string> InviteAsync(string owner, string email)
    {
        await RunAsync(async p => { await p.GetRequiredService<IAccountAccess>().InviteAsync(owner, email, "Invited user"); return true; });
        var id = await AccountAsync(a => a.RequestCodeAsync(email));
        return await AccountAsync(a => a.VerifyCodeAsync(id, Mail.Codes[email.ToUpperInvariant()]));
    }
    public async ValueTask DisposeAsync()
    {
        if (Services is not null) await Services.DisposeAsync();
        if (Directory.Exists(documentPath)) Directory.Delete(documentPath, recursive: true);
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(adminConnection); await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE {databaseName} WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }
}
