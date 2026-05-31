using Microsoft.EntityFrameworkCore;
using SpotifyTools.Domain;

namespace SpotifyTools.Infrastructure.Persistence;

public sealed class SpotifyDbContext : DbContext
{
    public DbSet<JobRun> JobRuns => Set<JobRun>();
    public DbSet<TrackHistory> TrackHistories => Set<TrackHistory>();
    public DbSet<ProcessedAlbum> ProcessedAlbums => Set<ProcessedAlbum>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    public SpotifyDbContext(DbContextOptions<SpotifyDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SpotifyDbContext).Assembly);
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseSqlite("Data Source=spotifyqueue.db;Cache=Shared;Journal Mode=WAL;");
        }
    }
}
