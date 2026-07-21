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
            Status = JobStatus.Running,
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

        async Task FinalizeAsync(string status, CancellationToken token, string? errorMessage = null)
        {
            jobRun.Status = status;
            jobRun.FinishedAt = DateTime.UtcNow;
            if (errorMessage is not null)
                jobRun.ErrorMessage = errorMessage;

            if (!ctx.DryRun)
                await db.SaveChangesAsync(token);
        }

        try
        {
            var playlistTracks = new List<Track>();
            await foreach (var track in ctx.Target.GetPlaylistTracksAsync(ctx.PlaylistId, ct))
                playlistTracks.Add(track);

            // Group occurrences by song identity (ISRC when available, else track id), so the same
            // recording under different catalog ids — different album editions — collapses into one
            // group. Keep the earliest-added copy per group; everything else is redundant.
            var keepers = new List<Track>();
            var idsToRemove = new HashSet<string>(StringComparer.Ordinal);
            var idsToReadd = new List<string>();
            var extraCopies = 0;
            var duplicateGroups = 0;

            // Union-find over every identity key (id / ISRC / artist+title): tracks that share ANY key
            // land in the same component, so an edition-twin chain (different id AND different ISRC but
            // same artist+title) still collapses into one song group.
            var parent = new Dictionary<string, string>(StringComparer.Ordinal);

            string Find(string x)
            {
                while (parent[x] != x)
                {
                    parent[x] = parent[parent[x]];
                    x = parent[x];
                }

                return x;
            }

            void Union(string a, string b)
            {
                var ra = Find(a);
                var rb = Find(b);
                if (ra != rb)
                    parent[ra] = rb;
            }

            foreach (var track in playlistTracks)
            {
                var keys = track.DedupKeys().ToList();
                foreach (var key in keys)
                    parent.TryAdd(key, key);
                for (var i = 1; i < keys.Count; i++)
                    Union(keys[0], keys[i]);
            }

            var components = playlistTracks
                .Select((track, index) => (track, index))
                .GroupBy(x => Find(x.track.DedupKeys().First()));

            foreach (var component in components)
            {
                var ordered = component
                    .OrderBy(x => x.track.AddedAt ?? DateTimeOffset.MaxValue)
                    .ThenBy(x => x.index)
                    .ToList();

                var keeper = ordered[0].track;
                keepers.Add(keeper);

                if (ordered.Count == 1)
                    continue;

                duplicateGroups++;
                extraCopies += ordered.Count - 1;

                // Remove every other catalog id in the group outright (each is a redundant edition).
                foreach (var id in ordered.Select(x => x.track.Id).Distinct())
                    if (id != keeper.Id)
                        idsToRemove.Add(id);

                // If the keeper's own id appears more than once (a true same-id duplicate), remove all
                // its copies and re-add one — the provider delete drops every occurrence of an id.
                if (ordered.Count(x => x.track.Id == keeper.Id) > 1)
                {
                    idsToRemove.Add(keeper.Id);
                    idsToReadd.Add(keeper.Id);
                }
            }

            if (idsToRemove.Count == 0)
            {
                Log.ReconcileNoDuplicates(logger);
            }
            else
            {
                Log.ReconcileFoundDuplicates(logger, duplicateGroups, extraCopies);

                if (ctx.DryRun)
                {
                    Log.DryRunWouldRemoveDuplicates(logger, extraCopies);
                }
                else
                {
                    // The two writes are not atomic — an interruption between them can leave copies
                    // missing. idsToReadd only holds true same-id duplicates; edition-twins are removed
                    // outright with no re-add, so their kept copy keeps its original position.
                    await ctx.Target.RemoveTracksFromPlaylistAsync(ctx.PlaylistId, idsToRemove, ct);
                    if (idsToReadd.Count > 0)
                        await ctx.Target.AddTracksToPlaylistAsync(ctx.PlaylistId, idsToReadd, ct);
                }
            }

            var backfilled = await BackfillHistoryAsync(ctx, jobRun.Id, keepers, ct);

            jobRun.TracksRemovedManual = extraCopies;
            jobRun.TracksAdded = backfilled;
            jobRun.QueueSizeAfter = keepers.Count;

            await FinalizeAsync(ctx.DryRun ? JobStatus.DryRun : JobStatus.Succeeded, ct);

            ReportSummary(ctx, jobRun, playlistTracks.Count, keepers.Count, duplicateGroups,
                extraCopies, backfilled);
        }
        catch (OperationCanceledException)
        {
            await FinalizeAsync(JobStatus.Partial, CancellationToken.None, "Cancelled by user");
            throw;
        }
        catch (Exception ex)
        {
            Log.JobFailed(logger, ex.Message, ex);
            await FinalizeAsync(JobStatus.Failed, CancellationToken.None, ex.Message);
            throw;
        }
    }

    private void ReportSummary(ReconcileRunContext ctx, JobRun jobRun, int playlistItems,
        int distinctSongs, int duplicateGroups, int extraCopies, int backfilled)
    {
        var duration = jobRun.FinishedAt.HasValue
            ? jobRun.FinishedAt.Value - jobRun.StartedAt
            : TimeSpan.Zero;

        if (ctx.DryRun)
            Log.DryRunActive(logger);

        Log.ReconcileCompleteHeader(logger);
        Log.SyncDuration(logger, duration.ToString(@"hh\:mm\:ss"));
        Log.SyncStatus(logger, jobRun.Status);
        Log.ReconcilePlaylistItems(logger, playlistItems);
        Log.ReconcileDistinctSongs(logger, distinctSongs);
        Log.ReconcileDuplicateGroups(logger, duplicateGroups);

        if (ctx.DryRun)
            Log.ReconcileCopiesToRemove(logger, extraCopies);
        else
            Log.ReconcileCopiesRemoved(logger, extraCopies);

        Log.ReconcileHistoryBackfilled(logger, backfilled);
        Log.QueueSize(logger, jobRun.QueueSizeAfter);
        Log.SyncCompleteFooter(logger);
    }

    private async Task<int> BackfillHistoryAsync(
        ReconcileRunContext ctx, Guid jobRunId, List<Track> keepers, CancellationToken ct)
    {
        var active = await db.TrackHistories
            .Where(x => x.Provider == ctx.ProviderName && x.RemovedAt == null)
            .Select(x => new { x.TrackId, x.TrackName, x.ArtistName, x.AlbumName, x.Isrc })
            .ToListAsync(ct);

        var presence = new TrackPresenceSet();
        foreach (var a in active)
            presence.Add(new Track(a.TrackId, a.TrackName, a.ArtistName, a.AlbumName, a.Isrc));

        var missing = keepers.Where(k => !presence.Contains(k)).ToList();
        if (missing.Count == 0)
            return 0;

        if (ctx.DryRun)
        {
            Log.ReconcileBackfilledHistory(logger, missing.Count);
            return missing.Count;
        }

        var now = DateTime.UtcNow;
        db.TrackHistories.AddRange(missing.Select(k => new TrackHistory
        {
            Id = Guid.CreateVersion7(),
            JobRunId = jobRunId,
            Provider = ctx.ProviderName,
            TrackId = k.Id,
            TrackName = k.Name,
            ArtistName = k.Artist,
            AlbumName = k.Album,
            Isrc = k.Isrc,
            AddedAt = now
        }));

        Log.ReconcileBackfilledHistory(logger, missing.Count);
        return missing.Count;
    }
}