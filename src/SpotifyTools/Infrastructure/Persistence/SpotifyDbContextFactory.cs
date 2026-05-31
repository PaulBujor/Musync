using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SpotifyTools.Infrastructure.Persistence;

public sealed class SpotifyDbContextFactory : IDesignTimeDbContextFactory<SpotifyDbContext>
{
    public SpotifyDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<SpotifyDbContext>();
        optionsBuilder.UseSqlite("Data Source=spotifyqueue.db");
        return new SpotifyDbContext(optionsBuilder.Options);
    }
}
