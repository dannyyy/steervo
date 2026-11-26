using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using Steervo.Application.Accounts;
using Steervo.Application.Documents;
using Steervo.Application.Evaluations;
using Steervo.Infrastructure.Data;
using Steervo.Infrastructure.Documents;
using Steervo.Infrastructure.Identity;

namespace Steervo.Tests;

/// <summary>A clock that only moves when a test sets <see cref="Now"/>.</summary>
public sealed class TestClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>Collects sign-in codes instead of sending mail; the last code per address wins.</summary>
public sealed class TestMail : IAccountMail
{
    public Dictionary<string, string> Codes { get; } = [];

    public Task SendCodeAsync(string email, string code)
    {
        Codes[email] = code;
        return Task.CompletedTask;
    }

    public Task SendInvitationAsync(string email) => Task.CompletedTask;
}

public sealed record TestActor(string Id) : ICurrentUser;

/// <summary>
/// A migrated, empty database of its own on one of the local PostgreSQL test servers, together
/// with the application services wired against it. Disposing drops the database again.
/// </summary>
public sealed class PostgresFixture : IAsyncDisposable
{
    public const string SetupToken = "test-only-setup-token-with-at-least-32-characters";

    private readonly string documentPath = Path.Combine(
        Path.GetTempPath(),
        "steervo-test-documents-" + Guid.NewGuid().ToString("N")
    );

    private readonly string databaseName = "steervo_test_" + Guid.NewGuid().ToString("N");

    private string adminConnection = "";

    public ServiceProvider Services { get; private set; } = null!;

    public TestClock Clock { get; } = new();

    public TestMail Mail { get; } = new();

    public static async Task<PostgresFixture> CreateAsync(int port, bool s3 = false)
    {
        var fixture = new PostgresFixture();
        fixture.adminConnection = AdminConnectionFor(port);

        await fixture.CreateDatabaseAsync();
        fixture.Services = fixture.BuildServices(s3);

        try
        {
            await fixture.ExecuteAsync(provider =>
                provider.GetRequiredService<AppDbContext>().Database.MigrateAsync()
            );
        }
        catch
        {
            // A fixture that never reaches the caller would otherwise leave its database behind.
            await fixture.DisposeAsync();
            throw;
        }

        return fixture;
    }

    public async Task<T> RunAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    /// <summary>Like <see cref="RunAsync{T}"/>, for work in a service scope that returns nothing.</summary>
    public Task ExecuteAsync(Func<IServiceProvider, Task> action)
    {
        return RunAsync(async provider =>
        {
            await action(provider);
            return true;
        });
    }

    /// <summary>Sends raw SQL past the application, to prove what the database itself rejects.</summary>
    public Task ExecuteSqlAsync(FormattableString sql)
    {
        return ExecuteAsync(provider =>
            provider.GetRequiredService<AppDbContext>().Database.ExecuteSqlInterpolatedAsync(sql)
        );
    }

    public Task<T> AccountAsync<T>(Func<IAccountAccess, Task<T>> action)
    {
        return RunAsync(provider => action(provider.GetRequiredService<IAccountAccess>()));
    }

    public Task<T> AsAsync<T>(string actor, Func<EvaluationWorkspace, Task<T>> action)
    {
        return RunAsync(provider =>
        {
            var workspace = new EvaluationWorkspace(
                provider.GetRequiredService<IEvaluationStore>(),
                new TestActor(actor),
                provider.GetRequiredService<IAccountDirectory>()
            );

            return action(workspace);
        });
    }

    /// <summary>Like <see cref="AsAsync{T}"/>, for workspace actions that return nothing.</summary>
    public Task ActAsync(string actor, Func<EvaluationWorkspace, Task> action)
    {
        return AsAsync(actor, async workspace =>
        {
            await action(workspace);
            return true;
        });
    }

    public Task<T> TransferAsync<T>(string actor, Func<EvaluationTransfer, Task<T>> action)
    {
        return RunAsync(provider =>
        {
            var store = provider.GetRequiredService<IEvaluationStore>();
            var current = new TestActor(actor);
            var workspace = new EvaluationWorkspace(
                store,
                current,
                provider.GetRequiredService<IAccountDirectory>()
            );

            return action(new EvaluationTransfer(workspace, store, current));
        });
    }

    public Task<T> DocumentsAsync<T>(string actor, Func<DocumentWorkspace, Task<T>> action)
    {
        return RunAsync(provider =>
        {
            var current = new TestActor(actor);
            var store = provider.GetRequiredService<IEvaluationStore>();
            var workspace = new EvaluationWorkspace(
                store,
                current,
                provider.GetRequiredService<IAccountDirectory>()
            );

            return action(
                new DocumentWorkspace(
                    workspace,
                    current,
                    provider.GetRequiredService<IDocumentStorage>()
                )
            );
        });
    }

    public Task<T> ArchiveAsync<T>(string actor, Func<EvaluationArchive, Task<T>> action)
    {
        return RunAsync(provider =>
        {
            var current = new TestActor(actor);
            var store = provider.GetRequiredService<IEvaluationStore>();
            var workspace = new EvaluationWorkspace(
                store,
                current,
                provider.GetRequiredService<IAccountDirectory>()
            );

            return action(
                new EvaluationArchive(
                    workspace,
                    new EvaluationTransfer(workspace, store, current),
                    store,
                    provider.GetRequiredService<IDocumentStorage>()
                )
            );
        });
    }

    /// <summary>Creates the first, primary account and returns its user id.</summary>
    public async Task<string> SetupAsync(string email = "owner@example.test")
    {
        var codeId = await AccountAsync(accounts =>
            accounts.BeginSetupAsync(SetupToken, email, "Owner")
        );

        return await AccountAsync(accounts =>
            accounts.VerifyCodeAsync(codeId, Mail.Codes[email.ToUpperInvariant()])
        );
    }

    /// <summary>Invites an address as <paramref name="owner"/>, signs it in and returns its user id.</summary>
    public async Task<string> InviteAsync(string owner, string email)
    {
        await ExecuteAsync(provider =>
            provider.GetRequiredService<IAccountAccess>().InviteAsync(owner, email, "Invited user")
        );

        var codeId = await AccountAsync(accounts => accounts.RequestCodeAsync(email));

        return await AccountAsync(accounts =>
            accounts.VerifyCodeAsync(codeId, Mail.Codes[email.ToUpperInvariant()])
        );
    }

    public async ValueTask DisposeAsync()
    {
        if (Services is not null)
        {
            await Services.DisposeAsync();
        }

        if (Directory.Exists(documentPath))
        {
            Directory.Delete(documentPath, recursive: true);
        }

        // Pooled connections would keep the database busy; FORCE covers whatever is still open.
        NpgsqlConnection.ClearAllPools();

        await using var connection = new NpgsqlConnection(adminConnection);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            $"DROP DATABASE {databaseName} WITH (FORCE)",
            connection
        );
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// The two ports stand for the two supported PostgreSQL versions; an environment variable per
    /// version can point the tests at a server somewhere else.
    /// </summary>
    private static string AdminConnectionFor(int port)
    {
        var configured = Environment.GetEnvironmentVariable(
            port == 55437 ? "STEERVO_TEST_POSTGRES17" : "STEERVO_TEST_POSTGRES18"
        );

        return configured
            ?? $"Host=localhost;Port={port};Database=steervo;Username=steervo;Password=steervo-local-only";
    }

    private async Task CreateDatabaseAsync()
    {
        await using var connection = new NpgsqlConnection(adminConnection);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand($"CREATE DATABASE {databaseName}", connection);
        await command.ExecuteNonQueryAsync();
    }

    private ServiceProvider BuildServices(bool s3)
    {
        var databaseConnection = new NpgsqlConnectionStringBuilder(adminConnection)
        {
            Database = databaseName,
        }.ConnectionString;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(databaseConnection));
        services
            .AddIdentityCore<AppUser>(options =>
            {
                options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
                options.User.RequireUniqueEmail = true;
            })
            .AddEntityFrameworkStores<AppDbContext>();
        services.Configure<SetupOptions>(options => options.Token = SetupToken);

        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton<IAccountMail>(Mail);
        services.AddScoped<IAccountAccess, AccountAccess>();
        services.AddScoped<IEvaluationStore, EvaluationStore>();
        services.AddScoped<IAccountDirectory, AccountDirectory>();

        services.AddSingleton(CreateDocumentObjects(s3));
        services.AddScoped<IDocumentStorage, DocumentStorage>();
        services.AddScoped<DocumentCleanup>();

        return services.BuildServiceProvider();
    }

    /// <summary>Documents go to the local S3 test server or, by default, to a temporary folder.</summary>
    private IDocumentObjects CreateDocumentObjects(bool s3)
    {
        if (!s3)
        {
            return new FileSystemDocumentObjects(
                Options.Create(new DocumentStorageOptions { Path = documentPath })
            );
        }

        var storage = new DocumentStorageOptions
        {
            Endpoint = "http://localhost:55439",
            Bucket = "steervo-tests",
            Region = "us-east-1",
            AccessKey = "test",
            SecretKey = "test",
        };

        return new S3DocumentObjects(Options.Create(storage));
    }
}
