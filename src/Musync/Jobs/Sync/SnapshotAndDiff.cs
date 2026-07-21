using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Musync.Domain;
using Musync.Infrastructure.Persistence;

namespace Musync.Jobs.Sync;

public sealed class SnapshotAndDiff(
    AppDbContext db,
    HybridCache cache,
    ILogger<SnapshotAndDiff> logger)
{
    public async Task<SnapshotResult> ExecuteAsync(JobRun jobRun, SyncRunContext ctx, CancellationToken ct)
    {
        Log.Step1Start(logger);

        var likedIndexTask = cache.GetOrCreateAsync(
            CacheKeys.LikedTracks(ctx.ProviderName),
            ct2 => BuildLikedIndexAsync(ctx, ct2),
            cancellationToken: ct).AsTask();

        var currentPlaylistTracks = await FetchPlaylistAsync(ctx, ct);
        var likedPresence = (await likedIndexTask).ToPresenceSet();

        // Playlist tracks that are already liked/saved — remove them from the queue. Match on id OR
        // ISRC so a liked song present under a different catalog id (another album edition) is pulled
        // too. Remove by playlist track id (the Tidal remove is occurrence-aware).
        var likedInPlaylist = currentPlaylistTracks
            .Where(t => likedPresence.Contains(t))
            .Select(t => t.Id)
            .Distinct()
            .ToList();

        if (likedInPlaylist.Count > 0)
        {
            if (ctx.DryRun)
            {
                Log.DryRunWouldRemoveLiked(logger, likedInPlaylist.Count);
            }
            else
            {
                Log.RemovingLikedTracks(logger, likedInPlaylist.Count);
                await ctx.Target.RemoveTracksFromPlaylistAsync(ctx.PlaylistId, likedInPlaylist, ct);

                var now = DateTime.UtcNow;
                // Chunk the IN-clause lookups so we never build an oversized SQL parameter list.
                const int historyLookupChunkSize = 500;
                var entries = new List<TrackHistory>();
                foreach (var batch in likedInPlaylist.Chunk(historyLookupChunkSize))
                {
                    var batchEntries = await db.TrackHistories
                        .Where(x => x.Provider == ctx.ProviderName
                                    && batch.Contains(x.TrackId) && x.RemovedAt == null)
                        .ToListAsync(ct);
                    entries.AddRange(batchEntries);
                }

                foreach (var entry in entries)
                {
                    entry.RemovedAt = now;
                    entry.RemovalReason = RemovalReasons.Liked;
                }
            }

            jobRun.TracksRemovedLiked = likedInPlaylist.Count;
        }

        // What still remains in the playlist (id + ISRC), for manual-removal detection.
        var removedLiked = new HashSet<string>(likedInPlaylist, StringComparer.Ordinal);
        var currentPresence = new TrackPresenceSet();
        foreach (var t in currentPlaylistTracks)
            if (!removedLiked.Contains(t.Id))
                currentPresence.Add(t);

        var activeHistory = await db.TrackHistories
            .Where(x => x.Provider == ctx.ProviderName && x.RemovedAt == null)
            .ToListAsync(ct);
        var manualRemovals = activeHistory
            .Where(h => !currentPresence.Contains(new Track(h.TrackId, h.TrackName, h.ArtistName, h.AlbumName, h.Isrc))
                        && !removedLiked.Contains(h.TrackId))
            .ToList();

        if (manualRemovals.Count > 0)
        {
            if (ctx.DryRun)
            {
                Log.DryRunWouldMarkManualRemovals(logger, manualRemovals.Count);
            }
            else
            {
                Log.MarkingManualRemovals(logger, manualRemovals.Count);
                var now = DateTime.UtcNow;
                foreach (var entry in manualRemovals)
                {
                    entry.RemovedAt = now;
                    entry.RemovalReason = RemovalReasons.Manual;
                }
            }

            jobRun.TracksRemovedManual = manualRemovals.Count;
        }

        if (!ctx.DryRun && (likedInPlaylist.Count > 0 || manualRemovals.Count > 0))
        {
            using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }

        var queueSizeAfterRemovals = currentPlaylistTracks
            .Where(t => !removedLiked.Contains(t.Id))
            .Select(t => t.PrimaryKey)
            .Distinct()
            .Count();

        return new SnapshotResult(currentPlaylistTracks, queueSizeAfterRemovals);
    }

    private static async ValueTask<LikedTracksIndex> BuildLikedIndexAsync(SyncRunContext ctx, CancellationToken ct)
    {
        var tracks = new List<Track>();
        await foreach (var t in ctx.Target.GetSavedTracksAsync(ct))
            tracks.Add(t);

        return LikedTracksIndex.FromTracks(tracks);
    }

    private async Task<List<Track>> FetchPlaylistAsync(SyncRunContext ctx, CancellationToken ct)
    {
        var tracks = new List<Track>();
        await foreach (var track in ctx.Target.GetPlaylistTracksAsync(ctx.PlaylistId, ct))
            tracks.Add(track);
        return tracks;
    }
}

/// <summary>State captured by snapshot &amp; diff and passed to the add-tracks step.</summary>
public sealed record SnapshotResult(IReadOnlyList<Track> CurrentPlaylistTracks, int QueueSizeAfterRemovals);