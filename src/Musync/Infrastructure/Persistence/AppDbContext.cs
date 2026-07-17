using Microsoft.EntityFrameworkCore;
using Musync.Domain;

namespace Musync.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<JobRun> JobRuns => Set<JobRun>();
    public DbSet<TrackHistory> TrackHistories => Set<TrackHistory>();
    public DbSet<ProcessedAlbum> ProcessedAlbums => Set<ProcessedAlbum>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<TrackMapping> TrackMappings => Set<TrackMapping>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // At most one active (not-yet-removed) history row per track, so a track already
        // in the playlist can never be recorded — and re-added — twice.
        modelBuilder.Entity<TrackHistory>()
            .HasIndex(x => new { x.Provider, x.TrackId })
            .HasFilter("\"RemovedAt\" IS NULL")
            .IsUnique();
    }
}
