using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Musync.Domain;
using Musync.Infrastructure.Persistence;

namespace Musync.Jobs;

public sealed class ReconcileQueueJob(
    AppDbContext db,
    ILogger<ReconcileQueueJob> logger)
{
    public async Task RunAsync(ReconcileRunContext ctx, CancellationToken ct)
    {
        var jobRun = new JobRun
        {
            Id = Guid.CreateVersion7(),
            StartedAt = DateTime.UtcNow,
            Status = "running",
            ProviderName = ctx.ProviderName,
            Command = "reconcile-queue",
            DryRun = ctx.DryRun
        };

        if (!ctx.DryRun)
        {
            db.JobRuns.Add(jobRun);
            await db.SaveChangesAsync(ct);
        }

        using var _ = logger.BeginScope(new { JobRunId = jobRun.Id.ToString() });
        Log.ReconcileStart(logger, ctx.PlaylistId);

        var playlistTrackIds = new List<string>();
        await foreach (var track in ctx.Target.GetPlaylistTracksAsync(ctx.PlaylistId, ct))
            playlistTrackIds.Add(track.Id);

        var duplicatedIds = playlistTrackIds
            .GroupBy(id => id)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        var extraCopies = playlistTrackIds.Count - playlistTrackIds.Distinct().Count();

        if (duplicatedIds.Count == 0)
        {
            Log.ReconcileNoDuplicates(logger);
        }
        else
        {
            Log.ReconcileFoundDuplicates(logger, duplicatedIds.Count, extraCopies);

            if (ctx.DryRun)
            {
                Log.DryRunWouldRemoveDuplicates(logger, extraCopies);
            }
            else
            {
                // Spotify's delete removes every occurrence of a uri, so drop all copies then re-add one.
                await ctx.Target.RemoveTracksFromPlaylistAsync(ctx.PlaylistId, duplicatedIds, ct);
                await ctx.Target.AddTracksToPlaylistAsync(ctx.PlaylistId, duplicatedIds, ct);
            }
        }

        var backfilled = await BackfillHistoryAsync(ctx, jobRun.Id, playlistTrackIds.Distinct().ToList(), ct);

        jobRun.TracksRemovedManual = extraCopies;
        jobRun.TracksAdded = backfilled;
        jobRun.QueueSizeAfter = playlistTrackIds.Distinct().Count();
        jobRun.Status = ctx.DryRun ? "dry-run" : "succeeded";
        jobRun.FinishedAt = DateTime.UtcNow;

        if (!ctx.DryRun)
            await db.SaveChangesAsync(ct);
    }

    private async Task<int> BackfillHistoryAsync(
        ReconcileRunContext ctx, Guid jobRunId, List<string> distinctPlaylistIds, CancellationToken ct)
    {
        var activeTrackIds = await db.TrackHistories
            .Where(x => x.Provider == ctx.ProviderName && x.RemovedAt == null)
            .Select(x => x.TrackId)
            .ToHashSetAsync(ct);

        var missing = distinctPlaylistIds.Where(id => !activeTrackIds.Contains(id)).ToList();
        if (missing.Count == 0)
            return 0;

        if (ctx.DryRun)
        {
            Log.ReconcileBackfilledHistory(logger, missing.Count);
            return missing.Count;
        }

        var now = DateTime.UtcNow;
        db.TrackHistories.AddRange(missing.Select(id => new TrackHistory
        {
            Id = Guid.CreateVersion7(),
            JobRunId = jobRunId,
            Provider = ctx.ProviderName,
            TrackId = id,
            AddedAt = now
        }));

        Log.ReconcileBackfilledHistory(logger, missing.Count);
        return missing.Count;
    }
}
