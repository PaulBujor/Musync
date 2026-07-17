using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Musync.Domain;
using Musync.Infrastructure.Persistence;

namespace Musync.Jobs.Import;

public sealed class AddToQueue(
    AppDbContext db,
    HybridCache cache,
    ILogger<AddToQueue> logger)
{
    public async Task ExecuteAsync(JobRun jobRun, ImportRunContext ctx,
        List<(string TargetTrackId, Track SourceTrack)> candidates, CancellationToken ct)
    {
        Log.ImportStep2Start(logger);

        if (candidates.Count == 0)
        {
            Log.NoMappedTracksToImport(logger);
            return;
        }

        var existingTrackIdsTask = db.TrackHistories
            .Where(x => x.Provider == ctx.TargetProviderName)
            .Select(x => x.TrackId)
            .Distinct()
            .ToHashSetAsync(ct);

        var currentPlaylistIds = new HashSet<string>();
        await foreach (var track in ctx.Target.GetPlaylistTracksAsync(ctx.PlaylistId, ct))
            currentPlaylistIds.Add(track.Id);

        var likedTrackIds = await cache.GetOrCreateAsync(
            CacheKeys.LikedTracks(ctx.TargetProviderName),
            async ct2 =>
            {
                var ids = new HashSet<string>();
                await foreach (var t in ctx.Target.GetSavedTracksAsync(ct2))
                    ids.Add(t.Id);
                return ids;
            },
            cancellationToken: ct);

        var existingTrackIds = await existingTrackIdsTask;

        var newTracks = new List<(string TargetTrackId, Track SourceTrack)>();
        foreach (var (targetId, sourceTrack) in candidates)
        {
            if (existingTrackIds.Contains(targetId) || currentPlaylistIds.Contains(targetId) ||
                likedTrackIds.Contains(targetId))
            {
                jobRun.TracksSkipped++;
                continue;
            }

            newTracks.Add((targetId, sourceTrack));
        }

        // Distinct source tracks can map to the same target id; keep one.
        newTracks = newTracks
            .GroupBy(t => t.TargetTrackId)
            .Select(g => g.First())
            .ToList();

        if (ctx.Limit.HasValue && newTracks.Count > ctx.Limit.Value)
        {
            newTracks = newTracks.Take(ctx.Limit.Value).ToList();
            Log.LimitReached(logger, ctx.Limit.Value);
        }

        if (newTracks.Count == 0)
        {
            Log.AllMappedTracksAlreadyInQueue(logger);
            return;
        }

        if (ctx.DryRun)
        {
            Log.DryRunWouldAdd(logger, newTracks.Count, ctx.PlaylistId);
            Log.DryRunWouldSaveHistory(logger, newTracks.Count);
        }
        else
        {
            var trackUris = newTracks.Select(t => t.TargetTrackId);
            Log.AddingTracks(logger, newTracks.Count, ctx.PlaylistId);
            await ctx.Target.AddTracksToPlaylistAsync(ctx.PlaylistId, trackUris, ct);

            var historyEntries = newTracks.Select(t => new TrackHistory
            {
                Id = Guid.CreateVersion7(),
                JobRunId = jobRun.Id,
                Provider = ctx.TargetProviderName,
                TrackId = t.TargetTrackId,
                TrackName = t.SourceTrack.Name,
                ArtistName = t.SourceTrack.Artist,
                // Source-provenance: may differ from target provider metadata
                AlbumName = t.SourceTrack.Album,
                AddedAt = DateTime.UtcNow
            });
            db.TrackHistories.AddRange(historyEntries);

            using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }

        jobRun.TracksAdded = newTracks.Count;
        jobRun.QueueSizeAfter = currentPlaylistIds.Count + newTracks.Count;
    }
}