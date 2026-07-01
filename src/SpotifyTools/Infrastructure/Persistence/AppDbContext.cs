using Microsoft.EntityFrameworkCore;
using SpotifyTools.Domain;

namespace SpotifyTools.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<JobRun> JobRuns => Set<JobRun>();
    public DbSet<TrackHistory> TrackHistories => Set<TrackHistory>();
    public DbSet<ProcessedAlbum> ProcessedAlbums => Set<ProcessedAlbum>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<TidalTrackMapping> TidalTrackMappings => Set<TidalTrackMapping>();
}
