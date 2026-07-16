using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Musync.Infrastructure.Persistence;

namespace Musync;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json")
            .AddEnvironmentVariables()
            .Build();

        var provider = configuration.GetValue<string>("Database:Provider") ?? "Postgres";
        var builder = new DbContextOptionsBuilder<AppDbContext>();

        switch (provider)
        {
            case "Sqlite":
                builder.UseSqlite(configuration.GetConnectionString("Sqlite"));
                break;
            default:
                builder.UseNpgsql(configuration.GetConnectionString("Postgres")!);
                break;
        }

        return new AppDbContext(builder.Options);
    }
}
