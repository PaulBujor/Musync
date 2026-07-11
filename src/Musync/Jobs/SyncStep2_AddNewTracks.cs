using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Musync.Domain;
using Musync.Infrastructure.Persistence;

namespace Musync.Jobs;

public sealed class SyncStep2_AddNewTracks(
    AppDbContext db,
    HybridCache cache,
    ILogger<SyncStep2_AddNewTracks> logger)
{
    public async Task ExecuteAsync(JobRun jobRun, SyncRunContext ctx, CancellationToken ct)
    {
        Log.Step2Start(logger);

        var likedTrackIdsTask = cache.GetOrCreateAsync(
            CacheKeys.LikedTracks(ctx.ProviderName),
            async ct2 =>
            {
                var ids = new HashSet<string>();
                await foreach (var t in ctx.Target.GetSavedTracksAsync(ct2))
                    ids.Add(t.Id);
                return ids;
            },
            cancellationToken: ct).AsTask();

        var historyTrackIdsTask = db.TrackHistories
            .Where(x => x.Provider == ctx.ProviderName)
            .Select(x => x.TrackId)
            .Distinct()
            .ToHashSetAsync(ct);

        var processedAlbumIdsTask = db.ProcessedAlbums
            .Where(x => x.Provider == ctx.ProviderName)
            .Select(a => a.AlbumId)
            .ToHashSetAsync(ct);

        var likedTrackIds = await likedTrackIdsTask;
        var historyTrackIds = await historyTrackIdsTask;
        var processedAlbumIds = await processedAlbumIdsTask;

        var newTracks = new List<Track>();
        var newlyProcessedAlbums = new List<ProcessedAlbum>();
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
                    return;

                Log.ProcessingAlbum(logger, album.Name, album.Artist);

                var albumTracks = new List<Track>();
                await foreach (var track in ctx.Target.GetAlbumTracksAsync(album.Id, album.Name, ct2))
                {
                    if (likedTrackIds.Contains(track.Id) || historyTrackIds.Contains(track.Id))
                    {
                        Interlocked.Increment(ref tracksSkipped);
                        continue;
                    }
                    albumTracks.Add(track);
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

        if (ctx.Limit.HasValue && limitHit)
        {
            Log.LimitReached(logger, ctx.Limit.Value);
        }

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
                    AddedAt = DateTime.UtcNow
                });

                db.TrackHistories.AddRange(historyEntries);
                await db.SaveChangesAsync(ct);
            }

            jobRun.TracksAdded = newTracks.Count;
        }

        jobRun.NewAlbumsEncountered = albumsProcessed;

        if (newlyProcessedAlbums.Count > 0)
        {
            if (ctx.DryRun)
            {
                Log.DryRunWouldSaveAlbums(logger, newlyProcessedAlbums.Count);
            }
            else
            {
                db.ProcessedAlbums.AddRange(newlyProcessedAlbums);
                await db.SaveChangesAsync(ct);
            }
        }

        var currentPlaylistTrackIds = new List<string>();
        await foreach (var track in ctx.Target.GetPlaylistTracksAsync(ctx.PlaylistId, ct))
            currentPlaylistTrackIds.Add(track.Id);
        jobRun.QueueSizeAfter = currentPlaylistTrackIds.Count;

        if (!ctx.DryRun)
            await db.SaveChangesAsync(ct);
    }
}
