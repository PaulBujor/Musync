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

public sealed class ImportTidalOrchestratorTests
{
    private static ServiceProvider BuildTestServices(
        List<Track>? tidalTracks = null,
        List<Track>? playlistTracks = null,
        List<Track>? spotifyLikedTracks = null,
        Dictionary<string, string>? trackMapping = null)
    {
        var services = new ServiceCollection();

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite("Data Source=:memory:"));

        services.AddHybridCache();

        var spotifyMock = new LocalMockMusicProvider(playlistTracks: playlistTracks, savedTracks: spotifyLikedTracks);
        services.AddSingleton<IMusicProvider>(spotifyMock);
        services.AddKeyedSingleton<IMusicProvider>("tidal", (_, _) =>
            new LocalMockMusicProvider(savedTracks: tidalTracks));
        services.AddKeyedSingleton<IMusicProvider>("spotify", spotifyMock);
        services.AddSingleton<ITrackMapper>(_ =>
            new LocalMockTrackMapper(trackMapping));

        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new SpotifyOptions
        {
            ClientId = "test",
            ClientSecret = "test",
            QueuePlaylistId = "test-playlist",
            MaxRetries = 1,
            MaxConcurrentRequests = 3
        }));

        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<ILogger<ImportTidalOrchestrator>>(NullLogger<ImportTidalOrchestrator>.Instance);
        services.AddSingleton<ILogger<ImportTidalStep1_FetchAndMap>>(NullLogger<ImportTidalStep1_FetchAndMap>.Instance);
        services.AddSingleton<ILogger<ImportTidalStep2_AddToQueue>>(NullLogger<ImportTidalStep2_AddToQueue>.Instance);
        services.AddSingleton<ILogger<ImportTidalStep3_GenerateReport>>(NullLogger<ImportTidalStep3_GenerateReport>.Instance);

        services.AddSingleton<ImportTidalStep1_FetchAndMap>();
        services.AddSingleton<ImportTidalStep2_AddToQueue>();
        services.AddSingleton<ImportTidalStep3_GenerateReport>();
        services.AddSingleton<ImportTidalOrchestrator>();

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
    public async Task RunAsync_NoTidalTracks_CompletesSuccessfully()
    {
        var sp = BuildTestServices();
        var orchestrator = sp.GetRequiredService<ImportTidalOrchestrator>();
        await orchestrator.RunAsync(CancellationToken.None);

        var db = sp.GetRequiredService<AppDbContext>();
        var latest = await GetLatestJobRunAsync(db);
        Assert.NotNull(latest);
        Assert.Equal("succeeded", latest.Status);
        Assert.Equal(0, latest.TracksAdded);
    }

    [Fact]
    public async Task RunAsync_WithTidalTracks_MapsAndAddsToQueue()
    {
        var tidalTracks = new List<Track>
        {
            new("tidal-1", "Track One", "Artist A", "Album A", "USRC10000001"),
            new("tidal-2", "Track Two", "Artist B", "Album B", "USRC10000002"),
        };

        var sp = BuildTestServices(tidalTracks: tidalTracks);
        var orchestrator = sp.GetRequiredService<ImportTidalOrchestrator>();
        await orchestrator.RunAsync(CancellationToken.None);

        var mock = (LocalMockMusicProvider)sp.GetRequiredKeyedService<IMusicProvider>("spotify");
        Assert.Contains(mock.PlaylistTracks, t => t.Id == "spotify-track-1");
        Assert.Contains(mock.PlaylistTracks, t => t.Id == "spotify-track-2");

        var db = sp.GetRequiredService<AppDbContext>();
        var history = await db.TrackHistories.ToListAsync();
        Assert.Equal(2, history.Count);
        Assert.Contains(history, h => h.SpotifyTrackId == "spotify-track-1");

        var mappings = await db.TidalTrackMappings.ToListAsync();
        Assert.Equal(2, mappings.Count);
        Assert.Contains(mappings, m => m.TidalTrackId == "tidal-1" && m.SpotifyTrackId == "spotify-track-1");
        Assert.Contains(mappings, m => m.TidalTrackId == "tidal-2" && m.SpotifyTrackId == "spotify-track-2");

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

        var sp = BuildTestServices(tidalTracks: tidalTracks);
        var db = sp.GetRequiredService<AppDbContext>();
        db.TrackHistories.Add(new TrackHistory
        {
            Id = Guid.CreateVersion7(),
            JobRunId = Guid.CreateVersion7(),
            SpotifyTrackId = "spotify-track-1",
            TrackName = "Track One",
            AddedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var orchestrator = sp.GetRequiredService<ImportTidalOrchestrator>();
        await orchestrator.RunAsync(CancellationToken.None);

        var mock = (LocalMockMusicProvider)sp.GetRequiredKeyedService<IMusicProvider>("spotify");
        Assert.Single(mock.PlaylistTracks);
        Assert.Contains(mock.PlaylistTracks, t => t.Id == "spotify-track-2");
        Assert.DoesNotContain(mock.PlaylistTracks, t => t.Id == "spotify-track-1");

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

        var sp = BuildTestServices(tidalTracks: tidalTracks);
        var orchestrator = sp.GetRequiredService<ImportTidalOrchestrator>();
        await orchestrator.RunAsync(CancellationToken.None);

        var mock = (LocalMockMusicProvider)sp.GetRequiredKeyedService<IMusicProvider>("spotify");
        Assert.Single(mock.PlaylistTracks);
        Assert.Contains(mock.PlaylistTracks, t => t.Id == "spotify-track-1");

        var db = sp.GetRequiredService<AppDbContext>();
        var mappings = await db.TidalTrackMappings.ToListAsync();
        Assert.Equal(2, mappings.Count);
        Assert.Contains(mappings, m => m.TidalTrackId == "tidal-unknown" && m.SpotifyTrackId == "");

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

        var sp = BuildTestServices(tidalTracks: tidalTracks);
        var db = sp.GetRequiredService<AppDbContext>();
        db.TidalTrackMappings.Add(new TidalTrackMapping
        {
            Id = Guid.CreateVersion7(),
            TidalTrackId = "tidal-1",
            SpotifyTrackId = "spotify-track-1",
            Isrc = "USRC10000001",
            FirstMappedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var orchestrator = sp.GetRequiredService<ImportTidalOrchestrator>();
        await orchestrator.RunAsync(CancellationToken.None);

        var mock = (LocalMockMusicProvider)sp.GetRequiredKeyedService<IMusicProvider>("spotify");
        Assert.Contains(mock.PlaylistTracks, t => t.Id == "spotify-track-1");

        var mappings = await db.TidalTrackMappings.ToListAsync();
        Assert.Single(mappings);

        var latest = await GetLatestJobRunAsync(db);
        Assert.NotNull(latest);
        Assert.Equal(1, latest.TracksAdded);
    }

    [Fact]
    public async Task RunAsync_TrackAlreadyLikedOnSpotify_SkipsIt()
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

        var sp = BuildTestServices(tidalTracks: tidalTracks, spotifyLikedTracks: spotifyLiked);
        var orchestrator = sp.GetRequiredService<ImportTidalOrchestrator>();
        await orchestrator.RunAsync(CancellationToken.None);

        var mock = (LocalMockMusicProvider)sp.GetRequiredKeyedService<IMusicProvider>("spotify");
        Assert.Single(mock.PlaylistTracks);
        Assert.Contains(mock.PlaylistTracks, t => t.Id == "spotify-track-2");
        Assert.DoesNotContain(mock.PlaylistTracks, t => t.Id == "spotify-track-1");

        var db = sp.GetRequiredService<AppDbContext>();
        var latest = await GetLatestJobRunAsync(db);
        Assert.NotNull(latest);
        Assert.Equal(1, latest.TracksAdded);
        Assert.Equal(1, latest.TracksSkipped);
    }
}
