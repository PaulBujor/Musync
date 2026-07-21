using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Musync.Domain;
using Musync.Infrastructure.Persistence;

namespace Musync.Jobs.Sync;

public sealed class AddNewTracks(
    AppDbContext db,
    HybridCache cache,
    ILogger<AddNewTracks> logger)
{
    public async Task<IReadOnlyList<SkippedAlbum>> ExecuteAsync(JobRun jobRun, SyncRunContext ctx,
        SnapshotResult snapshot, CancellationToken ct)
    {
        Log.Step2Start(logger);

        var likedIndexTask = cache.GetOrCreateAsync(
            CacheKeys.LikedTracks(ctx.ProviderName),
            ct2 => BuildLikedIndexAsync(ctx, ct2),
            cancellationToken: ct).AsTask();

        var historyTask = db.TrackHistories
            .Where(x => x.Provider == ctx.ProviderName)
            .Select(x => new { x.TrackId, x.TrackName, x.ArtistName, x.AlbumName, x.Isrc })
            .ToListAsync(ct);

        var processedAlbumsTask = db.ProcessedAlbums
            .Where(x => x.Provider == ctx.ProviderName)
            .ToListAsync(ct);

        var likedIndex = await likedIndexTask;
        var history = await historyTask;
        var processedAlbums = await processedAlbumsTask;

        // Combined "already have this song" set: liked ∪ history ∪ current playlist, matched by id,
        // ISRC OR artist+title. Built once up front, then only read inside the parallel loop (safe).
        var presence = likedIndex.ToPresenceSet();
        foreach (var h in history)
            presence.Add(new Track(h.TrackId, h.TrackName, h.ArtistName, h.AlbumName, h.Isrc));
        foreach (var t in snapshot.CurrentPlaylistTracks)
            presence.Add(t);
        var processedAlbumIds = processedAlbums.Select(a => a.AlbumId).ToHashSet();
        var processedAlbumDict = processedAlbums.ToDictionary(a => a.AlbumId);

        var newTracks = new List<Track>();
        var newlyProcessedAlbums = new List<ProcessedAlbum>();
        var updatedProcessedAlbums = new List<ProcessedAlbum>();
        var skippedAlbums = new List<SkippedAlbum>();
        var albumsProcessed = 0;
        var tracksSkipped = 0;
        var limitHit = false;
        var lockObj = new object();

        await Parallel.ForEachAsync(
            ctx.Target.GetSavedAlbumsAsync(ct),
            new ParallelOptions { MaxDegreeOfParallelism = ctx.MaxDegreeOfParallelism, CancellationToken = ct },
            async (album, ct2) =>
            {
                lock (lockObj)
                {
                    if (ctx.Limit.HasValue && albumsProcessed >= ctx.Limit.Value)
                    {
                        limitHit = true;
                        return;
                    }
                }

                if (processedAlbumIds.Contains(album.Id))
                {
                    lock (lockObj)
                    {
                        if (processedAlbumDict.TryGetValue(album.Id, out var existing))
                        {
                            existing.LastSeenAt = DateTime.UtcNow;
                            updatedProcessedAlbums.Add(existing);
                        }
                    }

                    return;
                }

                Log.ProcessingAlbum(logger, album.Name, album.Artist);

                var albumTracks = new List<Track>();
                try
                {
                    await foreach (var track in ctx.Target.GetAlbumTracksAsync(album.Id, album.Name, ct2))
                    {
                        if (presence.Contains(track))
                        {
                            Interlocked.Increment(ref tracksSkipped);
                            continue;
                        }

                        albumTracks.Add(track);
                    }
                }
                catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
                {
                    // A saved album can be region-unavailable or delisted and 404 on the catalog
                    // tracks endpoint. Skip it rather than aborting the whole run, and leave it
                    // unprocessed so a later run retries if it becomes available.
                    Log.AlbumTracksUnavailable(logger, album.Name, album.Artist);
                    lock (lockObj)
                        skippedAlbums.Add(new SkippedAlbum(album.Name, album.Artist, "unavailable (404)"));
                    return;
                }

                lock (lockObj)
                {
                    if (ctx.Limit.HasValue && albumsProcessed >= ctx.Limit.Value)
                    {
                        limitHit = true;
                        return;
                    }

                    newTracks.AddRange(albumTracks);
                    albumsProcessed++;

                    newlyProcessedAlbums.Add(new ProcessedAlbum
                    {
                        Id = Guid.CreateVersion7(),
                        Provider = ctx.ProviderName,
                        AlbumId = album.Id,
                        AlbumName = album.Name,
                        ArtistName = album.Artist,
                        FirstProcessedAt = DateTime.UtcNow,
                        LastSeenAt = DateTime.UtcNow
                    });
                }
            });

        jobRun.TracksSkipped = tracksSkipped;

        if (ctx.Limit.HasValue && limitHit) Log.LimitReached(logger, ctx.Limit.Value);

        // A track can appear on more than one saved album — and the same recording can appear under
        // different catalog ids across editions. Keep a single copy per song (matched by id or ISRC).
        var runDedup = new TrackPresenceSet();
        newTracks = newTracks.Where(runDedup.Add).ToList();

        if (newTracks.Count > 0)
        {
            if (ctx.DryRun)
            {
                Log.DryRunWouldAdd(logger, newTracks.Count, ctx.PlaylistId);
                Log.DryRunWouldSaveHistory(logger, newTracks.Count);
            }
            else
            {
                var trackUris = newTracks.Select(t => t.Id);
                Log.AddingTracks(logger, newTracks.Count, ctx.PlaylistId);
                await ctx.Target.AddTracksToPlaylistAsync(ctx.PlaylistId, trackUris, ct);

                var historyEntries = newTracks.Select(t => new TrackHistory
                {
                    Id = Guid.CreateVersion7(),
                    JobRunId = jobRun.Id,
                    Provider = ctx.ProviderName,
                    TrackId = t.Id,
                    TrackName = t.Name,
                    ArtistName = t.Artist,
                    AlbumName = t.Album,
                    Isrc = t.Isrc,
                    AddedAt = DateTime.UtcNow
                });

                db.TrackHistories.AddRange(historyEntries);
            }

            jobRun.TracksAdded = newTracks.Count;
        }

        jobRun.NewAlbumsEncountered = albumsProcessed;

        if (!ctx.DryRun)
        {
            using var transaction = await db.Database.BeginTransactionAsync(ct);

            if (newlyProcessedAlbums.Count > 0)
                db.ProcessedAlbums.AddRange(newlyProcessedAlbums);

            if (updatedProcessedAlbums.Count > 0)
                db.ProcessedAlbums.UpdateRange(updatedProcessedAlbums);

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        else if (newlyProcessedAlbums.Count > 0)
        {
            Log.DryRunWouldSaveAlbums(logger, newlyProcessedAlbums.Count);
        }

        jobRun.QueueSizeAfter = snapshot.QueueSizeAfterRemovals + newTracks.Count;

        return skippedAlbums;
    }

    private static async ValueTask<LikedTracksIndex> BuildLikedIndexAsync(SyncRunContext ctx, CancellationToken ct)
    {
        var tracks = new List<Track>();
        await foreach (var t in ctx.Target.GetSavedTracksAsync(ct))
            tracks.Add(t);

        return LikedTracksIndex.FromTracks(tracks);
    }
}

/// <summary>A saved album that couldn't be processed this run, with the reason, for the report.</summary>
public sealed record SkippedAlbum(string Name, string Artist, string Reason);