using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Musync.Domain;
using Musync.Infrastructure.Persistence;
using Musync.Jobs;
using Musync.Tests.Fakes;

namespace Musync.Tests.Jobs;

public sealed class ReconcileQueueJobTests
{
    private static ServiceProvider BuildTestServices()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options => options.UseSqlite("Data Source=:memory:"));
        services.AddSingleton<ReconcileQueueJob>();

        var sp = services.BuildServiceProvider();
        var db = sp.GetRequiredService<AppDbContext>();
        db.Database.OpenConnection();
        db.Database.EnsureCreated();
        return sp;
    }

    private static List<Track> DuplicatedPlaylist()
    {
        return
        [
            new("track-1", "One", "Artist", "Album"),
            new("track-1", "One", "Artist", "Album"),
            new("track-2", "Two", "Artist", "Album"),
            new("track-3", "Three", "Artist", "Album"),
            new("track-3", "Three", "Artist", "Album"),
            new("track-3", "Three", "Artist", "Album")
        ];
    }

    [Fact]
    public async Task RunAsync_RemovesDuplicatesAndBackfillsHistory()
    {
        var provider = new LocalMockMusicProvider(playlistTracks: DuplicatedPlaylist());
        var sp = BuildTestServices();
        var db = sp.GetRequiredService<AppDbContext>();
        var job = new ReconcileQueueJob(db, NullLogger<ReconcileQueueJob>.Instance);

        var ctx = new ReconcileRunContext("spotify", provider, "test-playlist", false);
        await job.RunAsync(ctx, CancellationToken.None);

        Assert.Equal(3, provider.PlaylistTracks.Count);
        Assert.Equal(1, provider.PlaylistTracks.Count(t => t.Id == "track-1"));
        Assert.Equal(1, provider.PlaylistTracks.Count(t => t.Id == "track-2"));
        Assert.Equal(1, provider.PlaylistTracks.Count(t => t.Id == "track-3"));

        var active = await db.TrackHistories.Where(h => h.RemovedAt == null).Select(h => h.TrackId).ToListAsync();
        Assert.Equal(3, active.Count);
        Assert.Contains("track-1", active);
        Assert.Contains("track-2", active);
        Assert.Contains("track-3", active);

        var latest = await db.JobRuns.OrderByDescending(x => x.StartedAt).FirstOrDefaultAsync();
        Assert.NotNull(latest);
        Assert.Equal("succeeded", latest.Status);
        Assert.Equal(3, latest.TracksRemovedManual);
        Assert.Equal(3, latest.QueueSizeAfter);
    }

    [Fact]
    public async Task RunAsync_Tidal_RemovesDuplicatesAndBackfillsHistory()
    {
        // The reconcile job is provider-agnostic. LocalMockMusicProvider.RemoveTracksFromPlaylistAsync
        // removes all occurrences of a given id, matching TidalMusicProvider's occurrence-aware delete,
        // so a "tidal" context exercises the same remove-all-then-re-add-one path end-to-end.
        var provider = new LocalMockMusicProvider(playlistTracks: DuplicatedPlaylist());
        var sp = BuildTestServices();
        var db = sp.GetRequiredService<AppDbContext>();
        var job = new ReconcileQueueJob(db, NullLogger<ReconcileQueueJob>.Instance);

        var ctx = new ReconcileRunContext("tidal", provider, "tidal-playlist", false);
        await job.RunAsync(ctx, CancellationToken.None);

        Assert.Equal(3, provider.PlaylistTracks.Count);
        Assert.Equal(1, provider.PlaylistTracks.Count(t => t.Id == "track-1"));
        Assert.Equal(1, provider.PlaylistTracks.Count(t => t.Id == "track-2"));
        Assert.Equal(1, provider.PlaylistTracks.Count(t => t.Id == "track-3"));

        var active = await db.TrackHistories
            .Where(h => h.RemovedAt == null && h.Provider == "tidal")
            .Select(h => h.TrackId).ToListAsync();
        Assert.Equal(3, active.Count);

        var latest = await db.JobRuns.OrderByDescending(x => x.StartedAt).FirstOrDefaultAsync();
        Assert.NotNull(latest);
        Assert.Equal("succeeded", latest.Status);
        Assert.Equal("tidal", latest.ProviderName);
        Assert.Equal(3, latest.TracksRemovedManual);
        Assert.Equal(3, latest.QueueSizeAfter);
    }

    [Fact]
    public async Task RunAsync_RemovesIsrcDuplicatesKeepingEarliestAdded()
    {
        // Same recording under two catalog ids (different album editions) shares an ISRC. Reconcile
        // must collapse them to the earliest-added copy and remove the other id outright (no re-add).
        var t0 = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var playlist = new List<Track>
        {
            new("id-deluxe", "2009", "Mac Miller", "Swimming (Deluxe)", "USQX91800001", t0.AddDays(5)),
            new("id-standard", "2009", "Mac Miller", "Swimming", "USQX91800001", t0),
            new("id-unique", "Other", "X", "Y", "USQX91800099", t0.AddDays(1))
        };
        var provider = new LocalMockMusicProvider(playlistTracks: playlist);
        var sp = BuildTestServices();
        var db = sp.GetRequiredService<AppDbContext>();
        var job = new ReconcileQueueJob(db, NullLogger<ReconcileQueueJob>.Instance);

        var ctx = new ReconcileRunContext("tidal", provider, "tidal-playlist", false);
        await job.RunAsync(ctx, CancellationToken.None);

        Assert.Equal(2, provider.PlaylistTracks.Count);
        // Earliest-added copy of the ISRC group is kept; the later edition-twin id is removed.
        Assert.Contains(provider.PlaylistTracks, t => t.Id == "id-standard");
        Assert.DoesNotContain(provider.PlaylistTracks, t => t.Id == "id-deluxe");
        Assert.Contains(provider.PlaylistTracks, t => t.Id == "id-unique");

        var latest = await db.JobRuns.OrderByDescending(x => x.StartedAt).FirstOrDefaultAsync();
        Assert.NotNull(latest);
        Assert.Equal("succeeded", latest.Status);
        Assert.Equal(1, latest.TracksRemovedManual);
        Assert.Equal(2, latest.QueueSizeAfter);

        // History is backfilled once per surviving song, carrying the ISRC.
        var active = await db.TrackHistories.Where(h => h.RemovedAt == null).ToListAsync();
        Assert.Equal(2, active.Count);
        Assert.Contains(active, h => h.TrackId == "id-standard" && h.Isrc == "USQX91800001");
    }

    [Fact]
    public async Task RunAsync_MergesSameArtistTitleAcrossDifferentIsrcs()
    {
        // Two pressings of the same song: same artist+title but DIFFERENT ISRCs and ids (the real
        // Swimming case). ISRC can't merge them; artist+title must. Keep the earliest-added.
        var t0 = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var playlist = new List<Track>
        {
            new("id-b", "2009", "Mac Miller", "Swimming", "USWB11801221", t0.AddDays(3)),
            new("id-a", "2009", "Mac Miller", "Swimming (Deluxe)", "USWB11801233", t0),
            new("id-c", "Self Care", "Mac Miller", "Swimming", "USWB11801227", t0.AddDays(1))
        };
        var provider = new LocalMockMusicProvider(playlistTracks: playlist);
        var sp = BuildTestServices();
        var db = sp.GetRequiredService<AppDbContext>();
        var job = new ReconcileQueueJob(db, NullLogger<ReconcileQueueJob>.Instance);

        var ctx = new ReconcileRunContext("tidal", provider, "tidal-playlist", false);
        await job.RunAsync(ctx, CancellationToken.None);

        Assert.Equal(2, provider.PlaylistTracks.Count);
        Assert.Contains(provider.PlaylistTracks, t => t.Id == "id-a");
        Assert.DoesNotContain(provider.PlaylistTracks, t => t.Id == "id-b");
        Assert.Contains(provider.PlaylistTracks, t => t.Id == "id-c");

        var latest = await db.JobRuns.OrderByDescending(x => x.StartedAt).FirstOrDefaultAsync();
        Assert.NotNull(latest);
        Assert.Equal(1, latest.TracksRemovedManual);
        Assert.Equal(2, latest.QueueSizeAfter);
    }

    [Fact]
    public async Task RunAsync_DryRun_DoesNotMutate()
    {
        var provider = new LocalMockMusicProvider(playlistTracks: DuplicatedPlaylist());
        var sp = BuildTestServices();
        var db = sp.GetRequiredService<AppDbContext>();
        var job = new ReconcileQueueJob(db, NullLogger<ReconcileQueueJob>.Instance);

        var ctx = new ReconcileRunContext("spotify", provider, "test-playlist", true);
        await job.RunAsync(ctx, CancellationToken.None);

        Assert.Equal(6, provider.PlaylistTracks.Count);
        Assert.Empty(await db.TrackHistories.ToListAsync());
        Assert.Empty(await db.JobRuns.ToListAsync());
    }

    [Fact]
    public async Task RunAsync_NoDuplicates_LeavesPlaylistUnchanged()
    {
        var playlist = new List<Track>
        {
            new("track-1", "One", "Artist", "Album"),
            new("track-2", "Two", "Artist", "Album")
        };
        var provider = new LocalMockMusicProvider(playlistTracks: playlist);
        var sp = BuildTestServices();
        var db = sp.GetRequiredService<AppDbContext>();
        var job = new ReconcileQueueJob(db, NullLogger<ReconcileQueueJob>.Instance);

        var ctx = new ReconcileRunContext("spotify", provider, "test-playlist", false);
        await job.RunAsync(ctx, CancellationToken.None);

        Assert.Equal(2, provider.PlaylistTracks.Count);

        var latest = await db.JobRuns.OrderByDescending(x => x.StartedAt).FirstOrDefaultAsync();
        Assert.NotNull(latest);
        Assert.Equal(0, latest.TracksRemovedManual);
        Assert.Equal(2, latest.TracksAdded);
    }
}