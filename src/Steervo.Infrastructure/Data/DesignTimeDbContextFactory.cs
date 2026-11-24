using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;

namespace Steervo.Infrastructure.Data;

/// <summary>
/// Creates the context for the EF Core command line tools, which run without the web host.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    private const string ConnectionVariable = "ConnectionStrings__Database";
    private const string LocalConnection =
        "Host=localhost;Port=55432;Database=steervo;Username=steervo";

    public AppDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable(ConnectionVariable) ?? LocalConnection;

        // The identity schema version shapes the model, so the tools need the application's setting.
        var services = new ServiceCollection()
            .Configure<IdentityOptions>(options =>
                options.Stores.SchemaVersion = IdentitySchemaVersions.Version3
            )
            .BuildServiceProvider();

        var contextOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseApplicationServiceProvider(services)
            .UseNpgsql(connection)
            .Options;

        return new AppDbContext(contextOptions);
    }
}
