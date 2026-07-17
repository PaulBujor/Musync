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