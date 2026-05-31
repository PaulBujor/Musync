using Microsoft.EntityFrameworkCore;
using SpotifyTools.Domain;

namespace SpotifyTools.Infrastructure.Persistence;

public sealed class SpotifyDbContext : DbContext
{
    public SpotifyDbContext() { }

    public SpotifyDbContext(DbContextOptions<SpotifyDbContext> options) : base(options) { }

    public DbSet<JobRun> JobRuns => Set<JobRun>();
    public DbSet<TrackHistory> TrackHistories => Set<TrackHistory>();
    public DbSet<ProcessedAlbum> ProcessedAlbums => Set<ProcessedAlbum>();
    public DbSet<RefreshTokens> RefreshTokens => Set<RefreshTokens>();
    public DbSet<TidalTrackMapping> TidalTrackMappings => Set<TidalTrackMapping>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
            optionsBuilder.UseSqlite("Data Source=spotifyqueue.db;Cache=Shared;");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RefreshTokens>()
            .ToTable("RefreshTokens");
    }
}
