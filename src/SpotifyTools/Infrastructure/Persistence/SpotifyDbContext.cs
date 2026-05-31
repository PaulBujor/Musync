using Microsoft.EntityFrameworkCore;
using SpotifyTools.Domain;

namespace SpotifyTools.Infrastructure.Persistence;

public sealed class SpotifyDbContext(DbContextOptions<SpotifyDbContext> options) : DbContext(options)
{
    public DbSet<JobRun> JobRuns => Set<JobRun>();
    public DbSet<TrackHistory> TrackHistories => Set<TrackHistory>();
    public DbSet<ProcessedAlbum> ProcessedAlbums => Set<ProcessedAlbum>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SpotifyDbContext).Assembly);
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
            optionsBuilder.UseSqlite("Data Source=spotifyqueue.db;Cache=Shared;Journal Mode=WAL;");
    }
}