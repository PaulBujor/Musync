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

        var provider = configuration.GetValue<string>("Database:Provider") ?? "Sqlite";
        var builder = new DbContextOptionsBuilder<AppDbContext>();

        switch (provider)
        {
            case "Sqlite":
                builder.UseSqlite(configuration.GetConnectionString("Sqlite"));
                break;
            case "Postgres":
                builder.UseNpgsql(configuration.GetConnectionString("Postgres")!);
                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported database provider: {provider}. Valid values: Sqlite, Postgres.");
        }

        return new AppDbContext(builder.Options);
    }
}