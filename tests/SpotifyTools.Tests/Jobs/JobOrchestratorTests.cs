using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SpotifyTools.Domain;
using SpotifyTools.Domain.Interfaces;
using SpotifyTools.Infrastructure.Persistence;
using SpotifyTools.Jobs;
using SpotifyTools.Options;
using SpotifyTools.Tests.Fakes;

namespace SpotifyTools.Tests.Jobs;

public sealed class JobOrchestratorTests
{
    private static ServiceProvider BuildTestServices(
        List<Album>? savedAlbums = null,
        HashSet<string>? likedTrackIds = null,
        List<Track>? playlistTracks = null)
    {
        var services = new ServiceCollection();

        services.AddDbContext<SpotifyDbContext>(options =>
            options.UseSqlite("Data Source=:memory:"));

        services.AddHybridCache();
        services.AddSingleton<ITrackHistoryRepository, TrackHistoryRepository>();
        services.AddSingleton<IJobRunRepository, JobRunRepository>();
        services.AddSingleton<IAppSettingsRepository, AppSettingsRepository>();
        services.AddSingleton<IMusicProvider>(_ =>
            new LocalMockMusicProvider(savedAlbums, likedTrackIds, playlistTracks));

        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new SpotifyOptions
        {
            ClientId = "test",
            ClientSecret = "test",
            QueuePlaylistId = "test-playlist",
            MaxRetries = 1,
            MaxConcurrentRequests = 3
        }));

        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<ILogger<JobOrchestrator>>(NullLogger<JobOrchestrator>.Instance);
        services.AddSingleton<ILogger<SyncStep1_SnapshotAndDiff>>(NullLogger<SyncStep1_SnapshotAndDiff>.Instance);
        services.AddSingleton<ILogger<SyncStep2_AddNewTracks>>(NullLogger<SyncStep2_AddNewTracks>.Instance);
        services.AddSingleton<ILogger<SyncStep4_GenerateReport>>(NullLogger<SyncStep4_GenerateReport>.Instance);

        services.AddSingleton<SyncStep1_SnapshotAndDiff>();
        services.AddSingleton<SyncStep2_AddNewTracks>();
        services.AddSingleton<SyncStep4_GenerateReport>();
        services.AddSingleton<JobOrchestrator>();

        var sp = services.BuildServiceProvider();

        var db = sp.GetRequiredService<SpotifyDbContext>();
        db.Database.OpenConnection();
        db.Database.EnsureCreated();

        return sp;
    }

    [Fact]
    public async Task RunAsync_NoSavedAlbums_CompletesSuccessfully()
    {
        var sp = BuildTestServices();
        var orchestrator = sp.GetRequiredService<JobOrchestrator>();
        await orchestrator.RunAsync(CancellationToken.None);

        var mock = (LocalMockMusicProvider)sp.GetRequiredService<IMusicProvider>();
        Assert.Empty(mock.PlaylistTracks);

        var db = sp.GetRequiredService<SpotifyDbContext>();
        Assert.Empty(await db.TrackHistories.ToListAsync());
        Assert.Empty(await db.ProcessedAlbums.ToListAsync());

        var jobRunRepo = sp.GetRequiredService<IJobRunRepository>();
        var latest = await jobRunRepo.GetLatestAsync(CancellationToken.None);
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

        var sp = BuildTestServices(savedAlbums: albums);
        var orchestrator = sp.GetRequiredService<JobOrchestrator>();
        await orchestrator.RunAsync(CancellationToken.None);

        var mock = (LocalMockMusicProvider)sp.GetRequiredService<IMusicProvider>();
        Assert.Contains(mock.PlaylistTracks, t => t.Id == "track-a1");
        Assert.Contains(mock.PlaylistTracks, t => t.Id == "track-a2");

        var db = sp.GetRequiredService<SpotifyDbContext>();
        var history = await db.TrackHistories.ToListAsync();
        Assert.Equal(2, history.Count);
        Assert.Contains(history, h => h.SpotifyTrackId == "track-a1");
        Assert.Contains(history, h => h.SpotifyTrackId == "track-a2");

        var processed = await db.ProcessedAlbums.ToListAsync();
        Assert.Single(processed);
        Assert.Equal("album-a", processed[0].SpotifyAlbumId);

        var jobRunRepo = sp.GetRequiredService<IJobRunRepository>();
        var latest = await jobRunRepo.GetLatestAsync(CancellationToken.None);
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

        var likedTrackIds = new HashSet<string> { "track-a1" };

        var sp = BuildTestServices(
            savedAlbums: albums,
            likedTrackIds: likedTrackIds);

        var orchestrator = sp.GetRequiredService<JobOrchestrator>();
        await orchestrator.RunAsync(CancellationToken.None);

        var mock = (LocalMockMusicProvider)sp.GetRequiredService<IMusicProvider>();
        Assert.Single(mock.PlaylistTracks);
        Assert.Contains(mock.PlaylistTracks, t => t.Id == "track-a2");
        Assert.DoesNotContain(mock.PlaylistTracks, t => t.Id == "track-a1");

        var db = sp.GetRequiredService<SpotifyDbContext>();
        var history = await db.TrackHistories.ToListAsync();
        Assert.Single(history);
        Assert.Equal("track-a2", history[0].SpotifyTrackId);

        var processed = await db.ProcessedAlbums.ToListAsync();
        Assert.Single(processed);
        Assert.Equal("album-a", processed[0].SpotifyAlbumId);

        var jobRunRepo = sp.GetRequiredService<IJobRunRepository>();
        var latest = await jobRunRepo.GetLatestAsync(CancellationToken.None);
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

        var sp = BuildTestServices(savedAlbums: albums);
        var db = sp.GetRequiredService<SpotifyDbContext>();
        db.ProcessedAlbums.Add(new ProcessedAlbum
        {
            Id = Guid.CreateVersion7(),
            SpotifyAlbumId = "album-a",
            AlbumName = "Album A",
            ArtistName = "Artist A",
            FirstProcessedAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var orchestrator = sp.GetRequiredService<JobOrchestrator>();
        await orchestrator.RunAsync(CancellationToken.None);

        var mock = (LocalMockMusicProvider)sp.GetRequiredService<IMusicProvider>();
        Assert.Empty(mock.PlaylistTracks);

        Assert.Empty(await db.TrackHistories.ToListAsync());

        var processed = await db.ProcessedAlbums.ToListAsync();
        Assert.Single(processed);

        var jobRunRepo = sp.GetRequiredService<IJobRunRepository>();
        var latest = await jobRunRepo.GetLatestAsync(CancellationToken.None);
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

        var sp = BuildTestServices(savedAlbums: albums);
        var historyRepo = sp.GetRequiredService<ITrackHistoryRepository>();
        await historyRepo.AddTrackHistoryAsync(
        [
            new TrackHistory
            {
                Id = Guid.CreateVersion7(),
                JobRunId = Guid.CreateVersion7(),
                SpotifyTrackId = "track-a1",
                TrackName = "Track A1",
                AddedAt = DateTime.UtcNow
            }
        ], CancellationToken.None);

        var orchestrator = sp.GetRequiredService<JobOrchestrator>();
        await orchestrator.RunAsync(CancellationToken.None);

        var mock = (LocalMockMusicProvider)sp.GetRequiredService<IMusicProvider>();
        Assert.Single(mock.PlaylistTracks);
        Assert.Contains(mock.PlaylistTracks, t => t.Id == "track-a2");
        Assert.DoesNotContain(mock.PlaylistTracks, t => t.Id == "track-a1");

        var db = sp.GetRequiredService<SpotifyDbContext>();
        var history = await db.TrackHistories.OrderBy(h => h.AddedAt).ToListAsync();
        Assert.Equal(2, history.Count);
        Assert.Equal("track-a1", history[0].SpotifyTrackId);
        Assert.Equal("track-a2", history[1].SpotifyTrackId);

        var processed = await db.ProcessedAlbums.ToListAsync();
        Assert.Single(processed);
        Assert.Equal("album-a", processed[0].SpotifyAlbumId);

        var jobRunRepo = sp.GetRequiredService<IJobRunRepository>();
        var latest = await jobRunRepo.GetLatestAsync(CancellationToken.None);
        Assert.NotNull(latest);
        Assert.Equal(1, latest.TracksAdded);
        Assert.Equal(1, latest.TracksSkipped);
    }

    [Fact]
    public async Task RunAsync_LikedTrackInPlaylist_RemovesIt()
    {
        var likedTrackIds = new HashSet<string> { "track-a1" };
        var playlistTracks = new List<Track>
        {
            new("track-a1", "Track A1", "Artist A", "Album A"),
            new("track-b1", "Track B1", "Artist B", "Album B")
        };

        var sp = BuildTestServices(
            savedAlbums: [],
            likedTrackIds: likedTrackIds,
            playlistTracks: playlistTracks);

        var orchestrator = sp.GetRequiredService<JobOrchestrator>();
        await orchestrator.RunAsync(CancellationToken.None);

        var mock = (LocalMockMusicProvider)sp.GetRequiredService<IMusicProvider>();
        Assert.Single(mock.PlaylistTracks);
        Assert.Contains(mock.PlaylistTracks, t => t.Id == "track-b1");
        Assert.DoesNotContain(mock.PlaylistTracks, t => t.Id == "track-a1");

        var db = sp.GetRequiredService<SpotifyDbContext>();
        var history = await db.TrackHistories.ToListAsync();
        Assert.Empty(history);

        var jobRunRepo = sp.GetRequiredService<IJobRunRepository>();
        var latest = await jobRunRepo.GetLatestAsync(CancellationToken.None);
        Assert.NotNull(latest);
        Assert.Equal(1, latest.TracksRemovedLiked);
        Assert.Equal("succeeded", latest.Status);
    }
}
