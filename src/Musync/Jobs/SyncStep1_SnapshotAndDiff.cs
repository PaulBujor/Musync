using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Musync.Domain.Interfaces;
using Musync.Infrastructure.Persistence;
using Musync.Options;

namespace Musync.Jobs;

public sealed class SyncStep1_SnapshotAndDiff(
    IMusicProvider music,
    AppDbContext db,
    HybridCache cache,
    IOptions<SpotifyOptions> options,
    ILogger<SyncStep1_SnapshotAndDiff> logger)
{
    private readonly SpotifyOptions _options = options.Value;

    public async Task ExecuteAsync(Domain.JobRun jobRun, CancellationToken ct)
    {
        Log.Step1Start(logger);

        var likedTrackIdsTask = cache.GetOrCreateAsync(
            CacheKeys.LikedTracks,
            async ct2 =>
            {
                var ids = new HashSet<string>();
                await foreach (var t in music.GetSavedTracksAsync(ct2))
                    ids.Add(t.Id);
                return ids;
            },
            cancellationToken: ct).AsTask();

        var currentPlaylistTracks = await FetchPlaylistAsync(ct);
        var likedTrackIds = await likedTrackIdsTask;

        var currentTrackIds = currentPlaylistTracks.Select(t => t.Id).ToHashSet();

        var likedInPlaylist = currentTrackIds.Where(likedTrackIds.Contains).ToList();
        if (likedInPlaylist.Count > 0)
        {
            Log.RemovingLikedTracks(logger, likedInPlaylist.Count);
            await music.RemoveTracksFromPlaylistAsync(_options.QueuePlaylistId, likedInPlaylist, ct);

            var now = DateTime.UtcNow;
            const int batchSize = 500;
            var entries = new List<Domain.TrackHistory>();
            foreach (var batch in likedInPlaylist.Chunk(batchSize))
            {
                var batchEntries = await db.TrackHistories
                    .Where(x => batch.Contains(x.SpotifyTrackId) && x.RemovedAt == null)
                    .ToListAsync(ct);
                entries.AddRange(batchEntries);
            }
            foreach (var entry in entries)
            {
                entry.RemovedAt = now;
                entry.RemovalReason = "liked";
            }
            await db.SaveChangesAsync(ct);

            jobRun.TracksRemovedLiked = likedInPlaylist.Count;
        }

        var activeHistory = await db.TrackHistories.Where(x => x.RemovedAt == null).ToListAsync(ct);
        var manualRemovals = activeHistory
            .Where(h => !currentTrackIds.Contains(h.SpotifyTrackId) && !likedInPlaylist.Contains(h.SpotifyTrackId))
            .ToList();

        if (manualRemovals.Count > 0)
        {
            Log.MarkingManualRemovals(logger, manualRemovals.Count);
            var now = DateTime.UtcNow;
            foreach (var entry in manualRemovals)
            {
                entry.RemovedAt = now;
                entry.RemovalReason = "manual";
            }
            await db.SaveChangesAsync(ct);
            jobRun.TracksRemovedManual = manualRemovals.Count;
        }

        jobRun.QueueSizeAfter = currentTrackIds.Count - likedInPlaylist.Count;
        db.JobRuns.Update(jobRun);
        await db.SaveChangesAsync(ct);
    }

    private async Task<List<Domain.Track>> FetchPlaylistAsync(CancellationToken ct)
    {
        var tracks = new List<Domain.Track>();
        await foreach (var track in music.GetPlaylistTracksAsync(_options.QueuePlaylistId, ct))
            tracks.Add(track);
        return tracks;
    }
}
