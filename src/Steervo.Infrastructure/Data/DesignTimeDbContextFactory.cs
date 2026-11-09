using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Steervo.Infrastructure.Data;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__Database")
            ?? "Host=localhost;Port=55432;Database=steervo;Username=steervo";
        var services = new ServiceCollection().Configure<IdentityOptions>(options => options.Stores.SchemaVersion = IdentitySchemaVersions.Version3).BuildServiceProvider();
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseApplicationServiceProvider(services).UseNpgsql(connection).Options);
    }
}
