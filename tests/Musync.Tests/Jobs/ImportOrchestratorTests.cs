using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Musync.Domain;
using Musync.Domain.Interfaces;
using Musync.Infrastructure.Persistence;
using Musync.Jobs;
using Musync.Options;
using Musync.Tests.Fakes;

namespace Musync.Tests.Jobs;

public sealed class ImportOrchestratorTests
{
    private static ServiceProvider BuildTestServices()
    {
        var services = new ServiceCollection();

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite("Data Source=:memory:"));

        services.AddHybridCache();

        services.AddSingleton<ILogger<ImportOrchestrator>>(NullLogger<ImportOrchestrator>.Instance);
        services.AddSingleton<ILogger<ImportStep1_FetchAndMap>>(NullLogger<ImportStep1_FetchAndMap>.Instance);
        services.AddSingleton<ILogger<ImportStep2_AddToQueue>>(NullLogger<ImportStep2_AddToQueue>.Instance);
        services.AddSingleton<ILogger<ImportStep3_GenerateReport>>(NullLogger<ImportStep3_GenerateReport>.Instance);

        services.AddSingleton<ImportStep1_FetchAndMap>();
        services.AddSingleton<ImportStep2_AddToQueue>();
        services.AddSingleton<ImportStep3_GenerateReport>();

        var sp = services.BuildServiceProvider();

        var db = sp.GetRequiredService<AppDbContext>();
        db.Database.OpenConnection();
        db.Database.EnsureCreated();

        return sp;
    }

    private static async Task<JobRun?> GetLatestJobRunAsync(AppDbContext db)
    {
        return await db.JobRuns.OrderByDescending(x => x.StartedAt).FirstOrDefaultAsync();
    }

    private static async Task<ServiceProvider> RunAsync(
        LocalMockMusicProvider sourceProvider,
        LocalMockMusicProvider targetProvider,
        ITrackMapper mapper,
        bool dryRun = false,
        int? limit = null)
    {
        var sp = BuildTestServices();
        var db = sp.GetRequiredService<AppDbContext>();
        var step1 = sp.GetRequiredService<ImportStep1_FetchAndMap>();
        var step2 = sp.GetRequiredService<ImportStep2_AddToQueue>();
        var step3 = sp.GetRequiredService<ImportStep3_GenerateReport>();
        var logger = NullLogger<ImportOrchestrator>.Instance;

        var ctx = new ImportRunContext(
            SourceProviderName: "tidal",
            TargetProviderName: "spotify",
            Source: sourceProvider,
            Target: targetProvider,
            Mapper: mapper,
            PlaylistId: "test-playlist",
            DryRun: dryRun,
            Limit: limit);

        var orchestrator = new ImportOrchestrator(db, step1, step2, step3, logger);
        await orchestrator.RunAsync(ctx, CancellationToken.None);

        return sp;
    }

    [Fact]
    public async Task RunAsync_NoSourceTracks_CompletesSuccessfully()
    {
        var source = new LocalMockMusicProvider();
        var target = new LocalMockMusicProvider();
        var mapper = new LocalMockTrackMapper();

        var sp = await RunAsync(source, target, mapper);
        var db = sp.GetRequiredService<AppDbContext>();

        var latest = await GetLatestJobRunAsync(db);
        Assert.NotNull(latest);
        Assert.Equal("succeeded", latest.Status);
        Assert.Equal(0, latest.TracksAdded);
    }

    [Fact]
    public async Task RunAsync_WithSourceTracks_MapsAndAddsToQueue()
    {
        var tidalTracks = new List<Track>
        {
            new("tidal-1", "Track One", "Artist A", "Album A", "USRC10000001"),
            new("tidal-2", "Track Two", "Artist B", "Album B", "USRC10000002"),
        };

        var source = new LocalMockMusicProvider(savedTracks: tidalTracks);
        var target = new LocalMockMusicProvider();
        var mapper = new LocalMockTrackMapper();

        var sp = await RunAsync(source, target, mapper);
        var db = sp.GetRequiredService<AppDbContext>();

        Assert.Contains(target.PlaylistTracks, t => t.Id == "spotify-track-1");
        Assert.Contains(target.PlaylistTracks, t => t.Id == "spotify-track-2");

        var history = await db.TrackHistories.ToListAsync();
        Assert.Equal(2, history.Count);
        Assert.Contains(history, h => h.TrackId == "spotify-track-1");

        var mappings = await db.TrackMappings.ToListAsync();
        Assert.Equal(2, mappings.Count);
        Assert.Contains(mappings, m => m.SourceTrackId == "tidal-1" && m.TargetTrackId == "spotify-track-1");
        Assert.Contains(mappings, m => m.SourceTrackId == "tidal-2" && m.TargetTrackId == "spotify-track-2");

        var latest = await GetLatestJobRunAsync(db);
        Assert.NotNull(latest);
        Assert.Equal("succeeded", latest.Status);
        Assert.Equal(2, latest.TracksAdded);
    }

    [Fact]
    public async Task RunAsync_TrackAlreadyInHistory_SkipsDuplicate()
    {
        var tidalTracks = new List<Track>
        {
            new("tidal-1", "Track One", "Artist A", "Album A", "USRC10000001"),
            new("tidal-2", "Track Two", "Artist B", "Album B", "USRC10000002"),
        };

        var source = new LocalMockMusicProvider(savedTracks: tidalTracks);
        var target = new LocalMockMusicProvider();
        var mapper = new LocalMockTrackMapper();

        var sp = BuildTestServices();
        var db = sp.GetRequiredService<AppDbContext>();
        db.TrackHistories.Add(new TrackHistory
        {
            Id = Guid.CreateVersion7(),
            JobRunId = Guid.CreateVersion7(),
            Provider = "spotify",
            TrackId = "spotify-track-1",
            TrackName = "Track One",
            AddedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var step1 = sp.GetRequiredService<ImportStep1_FetchAndMap>();
        var step2 = sp.GetRequiredService<ImportStep2_AddToQueue>();
        var step3 = sp.GetRequiredService<ImportStep3_GenerateReport>();
        var logger = NullLogger<ImportOrchestrator>.Instance;

        var ctx = new ImportRunContext(
            SourceProviderName: "tidal",
            TargetProviderName: "spotify",
            Source: source,
            Target: target,
            Mapper: mapper,
            PlaylistId: "test-playlist",
            DryRun: false,
            Limit: null);

        var orchestrator = new ImportOrchestrator(db, step1, step2, step3, logger);
        await orchestrator.RunAsync(ctx, CancellationToken.None);

        Assert.Single(target.PlaylistTracks);
        Assert.Contains(target.PlaylistTracks, t => t.Id == "spotify-track-2");
        Assert.DoesNotContain(target.PlaylistTracks, t => t.Id == "spotify-track-1");

        var latest = await GetLatestJobRunAsync(db);
        Assert.NotNull(latest);
        Assert.Equal(1, latest.TracksAdded);
        Assert.Equal(1, latest.TracksSkipped);
    }

    [Fact]
    public async Task RunAsync_UnmappableTrack_SkipsIt()
    {
        var tidalTracks = new List<Track>
        {
            new("tidal-1", "Track One", "Artist A", "Album A", "USRC10000001"),
            new("tidal-unknown", "Unknown Track", "Unknown Artist", "Album U", "USRC99999999"),
        };

        var source = new LocalMockMusicProvider(savedTracks: tidalTracks);
        var target = new LocalMockMusicProvider();
        var mapper = new LocalMockTrackMapper();

        var sp = await RunAsync(source, target, mapper);
        var db = sp.GetRequiredService<AppDbContext>();

        Assert.Single(target.PlaylistTracks);
        Assert.Contains(target.PlaylistTracks, t => t.Id == "spotify-track-1");

        var mappings = await db.TrackMappings.ToListAsync();
        Assert.Equal(2, mappings.Count);
        Assert.Contains(mappings, m => m.SourceTrackId == "tidal-unknown" && m.TargetTrackId == "");

        var latest = await GetLatestJobRunAsync(db);
        Assert.NotNull(latest);
        Assert.Equal(1, latest.TracksAdded);
    }

    [Fact]
    public async Task RunAsync_CachedMapping_DoesNotRequeryMapper()
    {
        var tidalTracks = new List<Track>
        {
            new("tidal-1", "Track One", "Artist A", "Album A", "USRC10000001"),
        };

        var sp = BuildTestServices();
        var db = sp.GetRequiredService<AppDbContext>();
        db.TrackMappings.Add(new TrackMapping
        {
            Id = Guid.CreateVersion7(),
            SourceProvider = "tidal",
            SourceTrackId = "tidal-1",
            TargetProvider = "spotify",
            TargetTrackId = "spotify-track-1",
            Isrc = "USRC10000001",
            FirstMappedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var source = new LocalMockMusicProvider(savedTracks: tidalTracks);
        var target = new LocalMockMusicProvider();
        var mapper = new LocalMockTrackMapper();

        var step1 = sp.GetRequiredService<ImportStep1_FetchAndMap>();
        var step2 = sp.GetRequiredService<ImportStep2_AddToQueue>();
        var step3 = sp.GetRequiredService<ImportStep3_GenerateReport>();
        var logger = NullLogger<ImportOrchestrator>.Instance;

        var ctx = new ImportRunContext(
            SourceProviderName: "tidal",
            TargetProviderName: "spotify",
            Source: source,
            Target: target,
            Mapper: mapper,
            PlaylistId: "test-playlist",
            DryRun: false,
            Limit: null);

        var orchestrator = new ImportOrchestrator(db, step1, step2, step3, logger);
        await orchestrator.RunAsync(ctx, CancellationToken.None);

        Assert.Contains(target.PlaylistTracks, t => t.Id == "spotify-track-1");

        var mappings = await db.TrackMappings.ToListAsync();
        Assert.Single(mappings);

        var latest = await GetLatestJobRunAsync(db);
        Assert.NotNull(latest);
        Assert.Equal(1, latest.TracksAdded);
    }

    [Fact]
    public async Task RunAsync_TrackAlreadyLikedOnTarget_SkipsIt()
    {
        var tidalTracks = new List<Track>
        {
            new("tidal-1", "Track One", "Artist A", "Album A", "USRC10000001"),
            new("tidal-2", "Track Two", "Artist B", "Album B", "USRC10000002"),
        };

        var spotifyLiked = new List<Track>
        {
            new("spotify-track-1", "Track One", "Artist A", "Album A", "USRC10000001"),
        };

        var source = new LocalMockMusicProvider(savedTracks: tidalTracks);
        var target = new LocalMockMusicProvider(savedTracks: spotifyLiked);
        var mapper = new LocalMockTrackMapper();

        var sp = await RunAsync(source, target, mapper);
        var db = sp.GetRequiredService<AppDbContext>();

        Assert.Single(target.PlaylistTracks);
        Assert.Contains(target.PlaylistTracks, t => t.Id == "spotify-track-2");
        Assert.DoesNotContain(target.PlaylistTracks, t => t.Id == "spotify-track-1");

        var latest = await GetLatestJobRunAsync(db);
        Assert.NotNull(latest);
        Assert.Equal(1, latest.TracksAdded);
        Assert.Equal(1, latest.TracksSkipped);
    }

    [Fact]
    public async Task RunAsync_DryRun_DoesNotMutate()
    {
        var tidalTracks = new List<Track>
        {
            new("tidal-1", "Track One", "Artist A", "Album A", "USRC10000001"),
        };

        var source = new LocalMockMusicProvider(savedTracks: tidalTracks);
        var target = new LocalMockMusicProvider();
        var mapper = new LocalMockTrackMapper();

        var sp = await RunAsync(source, target, mapper, dryRun: true);
        var db = sp.GetRequiredService<AppDbContext>();

        Assert.Empty(target.PlaylistTracks);

        Assert.Empty(await db.TrackHistories.ToListAsync());
        Assert.Empty(await db.TrackMappings.ToListAsync());

        var latest = await GetLatestJobRunAsync(db);
        Assert.NotNull(latest);
        Assert.Equal("dry-run", latest.Status);
        Assert.True(latest.DryRun);
    }

    [Fact]
    public async Task RunAsync_WithLimit_CapsTracksAdded()
    {
        var tidalTracks = new List<Track>
        {
            new("tidal-1", "Track One", "Artist A", "Album A", "USRC10000001"),
            new("tidal-2", "Track Two", "Artist B", "Album B", "USRC10000002"),
            new("tidal-3", "Track Three", "Artist C", "Album C", "USRC10000003"),
        };

        var source = new LocalMockMusicProvider(savedTracks: tidalTracks);
        var target = new LocalMockMusicProvider();
        var mapper = new LocalMockTrackMapper();

        var sp = await RunAsync(source, target, mapper, limit: 1);
        var db = sp.GetRequiredService<AppDbContext>();

        Assert.Single(target.PlaylistTracks);

        var latest = await GetLatestJobRunAsync(db);
        Assert.NotNull(latest);
        Assert.Equal(1, latest.TracksAdded);
        Assert.Equal(1, latest.Limit);
    }
}
