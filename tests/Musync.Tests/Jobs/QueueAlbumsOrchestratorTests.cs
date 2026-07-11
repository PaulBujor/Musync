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

public sealed class QueueAlbumsOrchestratorTests
{
    private static ServiceProvider BuildTestServices()
    {
        var services = new ServiceCollection();

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite("Data Source=:memory:"));

        services.AddHybridCache();

        services.AddSingleton<ILogger<QueueAlbumsOrchestrator>>(NullLogger<QueueAlbumsOrchestrator>.Instance);
        services.AddSingleton<ILogger<SyncStep1_SnapshotAndDiff>>(NullLogger<SyncStep1_SnapshotAndDiff>.Instance);
        services.AddSingleton<ILogger<SyncStep2_AddNewTracks>>(NullLogger<SyncStep2_AddNewTracks>.Instance);
        services.AddSingleton<ILogger<SyncStep3_GenerateReport>>(NullLogger<SyncStep3_GenerateReport>.Instance);

        services.AddSingleton<SyncStep1_SnapshotAndDiff>();
        services.AddSingleton<SyncStep2_AddNewTracks>();
        services.AddSingleton<SyncStep3_GenerateReport>();

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

    private static SyncRunContext CreateContext(
        LocalMockMusicProvider provider,
        bool dryRun = false,
        int? limit = null)
    {
        return new SyncRunContext(
            ProviderName: "spotify",
            Target: provider,
            PlaylistId: "test-playlist",
            MaxDegreeOfParallelism: 3,
            DryRun: dryRun,
            Limit: limit);
    }

    [Fact]
    public async Task RunAsync_NoSavedAlbums_CompletesSuccessfully()
    {
        var provider = new LocalMockMusicProvider();
        var sp = BuildTestServices();

        var db = sp.GetRequiredService<AppDbContext>();
        var step1 = sp.GetRequiredService<SyncStep1_SnapshotAndDiff>();
        var step2 = sp.GetRequiredService<SyncStep2_AddNewTracks>();
        var step3 = sp.GetRequiredService<SyncStep3_GenerateReport>();
        var logger = NullLogger<QueueAlbumsOrchestrator>.Instance;

        var ctx = CreateContext(provider);
        var orchestrator = new QueueAlbumsOrchestrator(db, step1, step2, step3, logger);
        await orchestrator.RunAsync(ctx, CancellationToken.None);

        Assert.Empty(provider.PlaylistTracks);

        Assert.Empty(await db.TrackHistories.ToListAsync());
        Assert.Empty(await db.ProcessedAlbums.ToListAsync());

        var latest = await GetLatestJobRunAsync(db);
        Assert.NotNull(latest);
        Assert.Equal("succeeded", latest.Status);
        Assert.Equal(0, latest.TracksAdded);
    }

    [Fact]
    public async Task RunAsync_WithNewAlbums_AddsTracks()
    {
        var albums = new List<Album>
        {
            new("album-a", "Album A", "Artist A")
        };

        var provider = new LocalMockMusicProvider(savedAlbums: albums);
        var sp = BuildTestServices();

        var db = sp.GetRequiredService<AppDbContext>();
        var step1 = sp.GetRequiredService<SyncStep1_SnapshotAndDiff>();
        var step2 = sp.GetRequiredService<SyncStep2_AddNewTracks>();
        var step3 = sp.GetRequiredService<SyncStep3_GenerateReport>();
        var logger = NullLogger<QueueAlbumsOrchestrator>.Instance;

        var ctx = CreateContext(provider);
        var orchestrator = new QueueAlbumsOrchestrator(db, step1, step2, step3, logger);
        await orchestrator.RunAsync(ctx, CancellationToken.None);

        Assert.Contains(provider.PlaylistTracks, t => t.Id == "track-a1");
        Assert.Contains(provider.PlaylistTracks, t => t.Id == "track-a2");

        var history = await db.TrackHistories.ToListAsync();
        Assert.Equal(2, history.Count);
        Assert.Contains(history, h => h.TrackId == "track-a1");
        Assert.Contains(history, h => h.TrackId == "track-a2");

        var processed = await db.ProcessedAlbums.ToListAsync();
        Assert.Single(processed);
        Assert.Equal("album-a", processed[0].AlbumId);

        var latest = await GetLatestJobRunAsync(db);
        Assert.NotNull(latest);
        Assert.Equal("succeeded", latest.Status);
        Assert.Equal(2, latest.TracksAdded);
        Assert.Equal(1, latest.NewAlbumsEncountered);
    }

    [Fact]
    public async Task RunAsync_LikedTrackIsSkipped()
    {
        var albums = new List<Album>
        {
            new("album-a", "Album A", "Artist A")
        };

        var savedTracks = new List<Track>
        {
            new("track-a1", "Track A1", "Artist A", "Album A")
        };

        var provider = new LocalMockMusicProvider(savedAlbums: albums, savedTracks: savedTracks);
        var sp = BuildTestServices();

        var db = sp.GetRequiredService<AppDbContext>();
        var step1 = sp.GetRequiredService<SyncStep1_SnapshotAndDiff>();
        var step2 = sp.GetRequiredService<SyncStep2_AddNewTracks>();
        var step3 = sp.GetRequiredService<SyncStep3_GenerateReport>();
        var logger = NullLogger<QueueAlbumsOrchestrator>.Instance;

        var ctx = CreateContext(provider);
        var orchestrator = new QueueAlbumsOrchestrator(db, step1, step2, step3, logger);
        await orchestrator.RunAsync(ctx, CancellationToken.None);

        Assert.Single(provider.PlaylistTracks);
        Assert.Contains(provider.PlaylistTracks, t => t.Id == "track-a2");
        Assert.DoesNotContain(provider.PlaylistTracks, t => t.Id == "track-a1");

        var history = await db.TrackHistories.ToListAsync();
        Assert.Single(history);
        Assert.Equal("track-a2", history[0].TrackId);

        var processed = await db.ProcessedAlbums.ToListAsync();
        Assert.Single(processed);
        Assert.Equal("album-a", processed[0].AlbumId);

        var latest = await GetLatestJobRunAsync(db);
        Assert.NotNull(latest);
        Assert.Equal(0, latest.TracksRemovedLiked);
        Assert.Equal(1, latest.TracksAdded);
        Assert.Equal(1, latest.TracksSkipped);
    }

    [Fact]
    public async Task RunAsync_AlbumAlreadyProcessed_SkipsTrackFetch()
    {
        var albums = new List<Album>
        {
            new("album-a", "Album A", "Artist A")
        };

        var provider = new LocalMockMusicProvider(savedAlbums: albums);
        var sp = BuildTestServices();

        var db = sp.GetRequiredService<AppDbContext>();
        db.ProcessedAlbums.Add(new ProcessedAlbum
        {
            Id = Guid.CreateVersion7(),
            Provider = "spotify",
            AlbumId = "album-a",
            AlbumName = "Album A",
            ArtistName = "Artist A",
            FirstProcessedAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var step1 = sp.GetRequiredService<SyncStep1_SnapshotAndDiff>();
        var step2 = sp.GetRequiredService<SyncStep2_AddNewTracks>();
        var step3 = sp.GetRequiredService<SyncStep3_GenerateReport>();
        var logger = NullLogger<QueueAlbumsOrchestrator>.Instance;

        var ctx = CreateContext(provider);
        var orchestrator = new QueueAlbumsOrchestrator(db, step1, step2, step3, logger);
        await orchestrator.RunAsync(ctx, CancellationToken.None);

        Assert.Empty(provider.PlaylistTracks);
        Assert.Empty(await db.TrackHistories.ToListAsync());

        var processed = await db.ProcessedAlbums.ToListAsync();
        Assert.Single(processed);

        var latest = await GetLatestJobRunAsync(db);
        Assert.NotNull(latest);
        Assert.Equal(0, latest.TracksAdded);
        Assert.Equal(0, latest.NewAlbumsEncountered);
    }

    [Fact]
    public async Task RunAsync_TrackAlreadyInHistory_SkipsIt()
    {
        var albums = new List<Album>
        {
            new("album-a", "Album A", "Artist A")
        };

        var provider = new LocalMockMusicProvider(savedAlbums: albums);
        var sp = BuildTestServices();

        var db = sp.GetRequiredService<AppDbContext>();
        db.TrackHistories.Add(new TrackHistory
        {
            Id = Guid.CreateVersion7(),
            JobRunId = Guid.CreateVersion7(),
            Provider = "spotify",
            TrackId = "track-a1",
            TrackName = "Track A1",
            AddedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var step1 = sp.GetRequiredService<SyncStep1_SnapshotAndDiff>();
        var step2 = sp.GetRequiredService<SyncStep2_AddNewTracks>();
        var step3 = sp.GetRequiredService<SyncStep3_GenerateReport>();
        var logger = NullLogger<QueueAlbumsOrchestrator>.Instance;

        var ctx = CreateContext(provider);
        var orchestrator = new QueueAlbumsOrchestrator(db, step1, step2, step3, logger);
        await orchestrator.RunAsync(ctx, CancellationToken.None);

        Assert.Single(provider.PlaylistTracks);
        Assert.Contains(provider.PlaylistTracks, t => t.Id == "track-a2");
        Assert.DoesNotContain(provider.PlaylistTracks, t => t.Id == "track-a1");

        var history = await db.TrackHistories.OrderBy(h => h.AddedAt).ToListAsync();
        Assert.Equal(2, history.Count);
        Assert.Equal("track-a1", history[0].TrackId);
        Assert.Equal("track-a2", history[1].TrackId);

        var processed = await db.ProcessedAlbums.ToListAsync();
        Assert.Single(processed);
        Assert.Equal("album-a", processed[0].AlbumId);

        var latest = await GetLatestJobRunAsync(db);
        Assert.NotNull(latest);
        Assert.Equal(1, latest.TracksAdded);
        Assert.Equal(1, latest.TracksSkipped);
    }

    [Fact]
    public async Task RunAsync_LikedTrackInPlaylist_RemovesIt()
    {
        var savedTracks = new List<Track>
        {
            new("track-a1", "Track A1", "Artist A", "Album A")
        };
        var playlistTracks = new List<Track>
        {
            new("track-a1", "Track A1", "Artist A", "Album A"),
            new("track-b1", "Track B1", "Artist B", "Album B")
        };

        var provider = new LocalMockMusicProvider(
            playlistTracks: playlistTracks,
            savedTracks: savedTracks);
        var sp = BuildTestServices();

        var db = sp.GetRequiredService<AppDbContext>();
        var step1 = sp.GetRequiredService<SyncStep1_SnapshotAndDiff>();
        var step2 = sp.GetRequiredService<SyncStep2_AddNewTracks>();
        var step3 = sp.GetRequiredService<SyncStep3_GenerateReport>();
        var logger = NullLogger<QueueAlbumsOrchestrator>.Instance;

        var ctx = CreateContext(provider);
        var orchestrator = new QueueAlbumsOrchestrator(db, step1, step2, step3, logger);
        await orchestrator.RunAsync(ctx, CancellationToken.None);

        Assert.Single(provider.PlaylistTracks);
        Assert.Contains(provider.PlaylistTracks, t => t.Id == "track-b1");
        Assert.DoesNotContain(provider.PlaylistTracks, t => t.Id == "track-a1");

        var history = await db.TrackHistories.ToListAsync();
        Assert.Empty(history);

        var latest = await GetLatestJobRunAsync(db);
        Assert.NotNull(latest);
        Assert.Equal(1, latest.TracksRemovedLiked);
        Assert.Equal("succeeded", latest.Status);
    }

    [Fact]
    public async Task RunAsync_DryRun_DoesNotMutate()
    {
        var albums = new List<Album>
        {
            new("album-a", "Album A", "Artist A")
        };
        var savedTracks = new List<Track>
        {
            new("track-a1", "Track A1", "Artist A", "Album A")
        };
        var playlistTracks = new List<Track>
        {
            new("track-x1", "Track X1", "Artist X", "Album X")
        };

        var provider = new LocalMockMusicProvider(
            savedAlbums: albums,
            playlistTracks: playlistTracks,
            savedTracks: savedTracks);
        var sp = BuildTestServices();

        var db = sp.GetRequiredService<AppDbContext>();
        var step1 = sp.GetRequiredService<SyncStep1_SnapshotAndDiff>();
        var step2 = sp.GetRequiredService<SyncStep2_AddNewTracks>();
        var step3 = sp.GetRequiredService<SyncStep3_GenerateReport>();
        var logger = NullLogger<QueueAlbumsOrchestrator>.Instance;

        var ctx = CreateContext(provider, dryRun: true);
        var orchestrator = new QueueAlbumsOrchestrator(db, step1, step2, step3, logger);
        await orchestrator.RunAsync(ctx, CancellationToken.None);

        // Playlist unchanged — no tracks added, no tracks removed
        Assert.Single(provider.PlaylistTracks);
        Assert.Contains(provider.PlaylistTracks, t => t.Id == "track-x1");

        // No DB mutations
        Assert.Empty(await db.TrackHistories.ToListAsync());
        Assert.Empty(await db.ProcessedAlbums.ToListAsync());

        var latest = await GetLatestJobRunAsync(db);
        Assert.NotNull(latest);
        Assert.Equal("dry-run", latest.Status);
        Assert.True(latest.DryRun);
    }

    [Fact]
    public async Task RunAsync_WithLimit_ProcessesMaxAlbums()
    {
        var albums = new List<Album>
        {
            new("album-a", "Album A", "Artist A"),
            new("album-b", "Album B", "Artist B"),
            new("album-c", "Album C", "Artist C")
        };

        var provider = new LocalMockMusicProvider(savedAlbums: albums);
        var sp = BuildTestServices();

        var db = sp.GetRequiredService<AppDbContext>();
        var step1 = sp.GetRequiredService<SyncStep1_SnapshotAndDiff>();
        var step2 = sp.GetRequiredService<SyncStep2_AddNewTracks>();
        var step3 = sp.GetRequiredService<SyncStep3_GenerateReport>();
        var logger = NullLogger<QueueAlbumsOrchestrator>.Instance;

        var ctx = CreateContext(provider, limit: 1);
        var orchestrator = new QueueAlbumsOrchestrator(db, step1, step2, step3, logger);
        await orchestrator.RunAsync(ctx, CancellationToken.None);

        var processed = await db.ProcessedAlbums.ToListAsync();
        Assert.Single(processed);

        var latest = await GetLatestJobRunAsync(db);
        Assert.NotNull(latest);
        Assert.Equal(1, latest.Limit);
        Assert.Equal(1, latest.NewAlbumsEncountered);
    }

    [Fact]
    public async Task RunAsync_Limit0_ProcessesNothing()
    {
        var albums = new List<Album>
        {
            new("album-a", "Album A", "Artist A")
        };

        var provider = new LocalMockMusicProvider(savedAlbums: albums);
        var sp = BuildTestServices();

        var db = sp.GetRequiredService<AppDbContext>();
        var step1 = sp.GetRequiredService<SyncStep1_SnapshotAndDiff>();
        var step2 = sp.GetRequiredService<SyncStep2_AddNewTracks>();
        var step3 = sp.GetRequiredService<SyncStep3_GenerateReport>();
        var logger = NullLogger<QueueAlbumsOrchestrator>.Instance;

        var ctx = CreateContext(provider, limit: 0);
        var orchestrator = new QueueAlbumsOrchestrator(db, step1, step2, step3, logger);
        await orchestrator.RunAsync(ctx, CancellationToken.None);

        Assert.Empty(provider.PlaylistTracks);
        Assert.Empty(await db.TrackHistories.ToListAsync());
        Assert.Empty(await db.ProcessedAlbums.ToListAsync());

        var latest = await GetLatestJobRunAsync(db);
        Assert.NotNull(latest);
        Assert.Equal(0, latest.TracksAdded);
    }
}
