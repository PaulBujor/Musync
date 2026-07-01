using Microsoft.EntityFrameworkCore;
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
        List<Track>? playlistTracks = null,
        List<Track>? savedTracks = null)
    {
        var services = new ServiceCollection();

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite("Data Source=:memory:"));

        services.AddHybridCache();
        services.AddSingleton<IMusicProvider>(_ =>
            new LocalMockMusicProvider(savedAlbums, playlistTracks, savedTracks));

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
        services.AddSingleton<ILogger<SyncStep3_GenerateReport>>(NullLogger<SyncStep3_GenerateReport>.Instance);

        services.AddSingleton<SyncStep1_SnapshotAndDiff>();
        services.AddSingleton<SyncStep2_AddNewTracks>();
        services.AddSingleton<SyncStep3_GenerateReport>();
        services.AddSingleton<JobOrchestrator>();

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

    [Fact]
    public async Task RunAsync_NoSavedAlbums_CompletesSuccessfully()
    {
        var sp = BuildTestServices();
        var orchestrator = sp.GetRequiredService<JobOrchestrator>();
        await orchestrator.RunAsync(CancellationToken.None);

        var mock = (LocalMockMusicProvider)sp.GetRequiredService<IMusicProvider>();
        Assert.Empty(mock.PlaylistTracks);

        var db = sp.GetRequiredService<AppDbContext>();
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

        var sp = BuildTestServices(albums);
        var orchestrator = sp.GetRequiredService<JobOrchestrator>();
        await orchestrator.RunAsync(CancellationToken.None);

        var mock = (LocalMockMusicProvider)sp.GetRequiredService<IMusicProvider>();
        Assert.Contains(mock.PlaylistTracks, t => t.Id == "track-a1");
        Assert.Contains(mock.PlaylistTracks, t => t.Id == "track-a2");

        var db = sp.GetRequiredService<AppDbContext>();
        var history = await db.TrackHistories.ToListAsync();
        Assert.Equal(2, history.Count);
        Assert.Contains(history, h => h.SpotifyTrackId == "track-a1");
        Assert.Contains(history, h => h.SpotifyTrackId == "track-a2");

        var processed = await db.ProcessedAlbums.ToListAsync();
        Assert.Single(processed);
        Assert.Equal("album-a", processed[0].SpotifyAlbumId);

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

        var sp = BuildTestServices(
            albums,
            savedTracks: savedTracks);

        var orchestrator = sp.GetRequiredService<JobOrchestrator>();
        await orchestrator.RunAsync(CancellationToken.None);

        var mock = (LocalMockMusicProvider)sp.GetRequiredService<IMusicProvider>();
        Assert.Single(mock.PlaylistTracks);
        Assert.Contains(mock.PlaylistTracks, t => t.Id == "track-a2");
        Assert.DoesNotContain(mock.PlaylistTracks, t => t.Id == "track-a1");

        var db = sp.GetRequiredService<AppDbContext>();
        var history = await db.TrackHistories.ToListAsync();
        Assert.Single(history);
        Assert.Equal("track-a2", history[0].SpotifyTrackId);

        var processed = await db.ProcessedAlbums.ToListAsync();
        Assert.Single(processed);
        Assert.Equal("album-a", processed[0].SpotifyAlbumId);

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

        var sp = BuildTestServices(albums);
        var db = sp.GetRequiredService<AppDbContext>();
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

        var sp = BuildTestServices(albums);
        var db = sp.GetRequiredService<AppDbContext>();
        db.TrackHistories.Add(new TrackHistory
        {
            Id = Guid.CreateVersion7(),
            JobRunId = Guid.CreateVersion7(),
            SpotifyTrackId = "track-a1",
            TrackName = "Track A1",
            AddedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var orchestrator = sp.GetRequiredService<JobOrchestrator>();
        await orchestrator.RunAsync(CancellationToken.None);

        var mock = (LocalMockMusicProvider)sp.GetRequiredService<IMusicProvider>();
        Assert.Single(mock.PlaylistTracks);
        Assert.Contains(mock.PlaylistTracks, t => t.Id == "track-a2");
        Assert.DoesNotContain(mock.PlaylistTracks, t => t.Id == "track-a1");

        var history = await db.TrackHistories.OrderBy(h => h.AddedAt).ToListAsync();
        Assert.Equal(2, history.Count);
        Assert.Equal("track-a1", history[0].SpotifyTrackId);
        Assert.Equal("track-a2", history[1].SpotifyTrackId);

        var processed = await db.ProcessedAlbums.ToListAsync();
        Assert.Single(processed);
        Assert.Equal("album-a", processed[0].SpotifyAlbumId);

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

        var sp = BuildTestServices(
            [],
            playlistTracks,
            savedTracks);

        var orchestrator = sp.GetRequiredService<JobOrchestrator>();
        await orchestrator.RunAsync(CancellationToken.None);

        var mock = (LocalMockMusicProvider)sp.GetRequiredService<IMusicProvider>();
        Assert.Single(mock.PlaylistTracks);
        Assert.Contains(mock.PlaylistTracks, t => t.Id == "track-b1");
        Assert.DoesNotContain(mock.PlaylistTracks, t => t.Id == "track-a1");

        var db = sp.GetRequiredService<AppDbContext>();
        var history = await db.TrackHistories.ToListAsync();
        Assert.Empty(history);

        var latest = await GetLatestJobRunAsync(db);
        Assert.NotNull(latest);
        Assert.Equal(1, latest.TracksRemovedLiked);
        Assert.Equal("succeeded", latest.Status);
    }
}
