using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Musync.Domain;

namespace Musync.Infrastructure.Persistence;

public sealed class AppDbContext : DbContext
{
    // Production registers Data Protection (see Program.cs) so refresh tokens survive restarts.
    // When no provider is injected (design-time tooling, tests) an in-process ephemeral key ring
    // is used instead, which round-trips within a single run but not across processes.
    private static readonly IDataProtectionProvider EphemeralFallback = new EphemeralDataProtectionProvider();

    private readonly IDataProtector _tokenProtector;

    public AppDbContext(DbContextOptions<AppDbContext> options, IDataProtectionProvider? dataProtectionProvider = null)
        : base(options)
    {
        _tokenProtector = (dataProtectionProvider ?? EphemeralFallback).CreateProtector("Musync.RefreshToken.v1");
    }

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

        // Refresh tokens are the most sensitive value at rest; encrypt the column transparently.
        var protector = _tokenProtector;
        var tokenEncryption = new ValueConverter<string, string>(
            plaintext => TokenProtection.Protect(protector, plaintext),
            stored => TokenProtection.Unprotect(protector, stored));

        modelBuilder.Entity<RefreshToken>()
            .Property(x => x.Token)
            .HasConversion(tokenEncryption);
    }
}
