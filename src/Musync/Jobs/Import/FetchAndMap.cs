using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Musync.Domain;
using Musync.Infrastructure.Persistence;

namespace Musync.Jobs.Import;

public sealed class FetchAndMap(
    AppDbContext db,
    ILogger<FetchAndMap> logger)
{
    public async Task<List<(string TargetTrackId, Track SourceTrack)>> ExecuteAsync(JobRun jobRun, ImportRunContext ctx,
        CancellationToken ct)
    {
        Log.ImportStep1Start(logger);

        var existingMappings = await db.TrackMappings
            .Where(m => m.SourceProvider == ctx.SourceProviderName && m.TargetProvider == ctx.TargetProviderName)
            .ToDictionaryAsync(m => m.SourceTrackId, m => m.TargetTrackId, ct);

        var candidates = new List<(string TargetTrackId, Track SourceTrack)>();

        await foreach (var sourceTrack in ctx.Source.GetSavedTracksAsync(ct))
        {
            var sourceId = sourceTrack.Id;

            if (existingMappings.TryGetValue(sourceId, out var cachedTargetId))
            {
                // An empty target is a cached negative — the track wasn't found last run, so skip
                // re-searching it. A non-empty target is a cached hit.
                if (!string.IsNullOrEmpty(cachedTargetId))
                    candidates.Add((cachedTargetId, sourceTrack));
                continue;
            }

            var match = await ctx.Mapper.FindMatchAsync(sourceTrack, ct);

            switch (match.Outcome)
            {
                case TrackMatchOutcome.Matched:
                    candidates.Add((match.TargetTrackId!, sourceTrack));
                    if (!ctx.DryRun)
                        db.TrackMappings.Add(NewMapping(ctx, sourceId, match.TargetTrackId!, sourceTrack.Isrc));
                    break;

                case TrackMatchOutcome.NotFound:
                    Log.TrackNotMapped(logger, sourceTrack.Name, sourceTrack.Artist);
                    // Cache the negative (empty target) so we don't re-search it next run.
                    if (!ctx.DryRun)
                        db.TrackMappings.Add(NewMapping(ctx, sourceId, "", sourceTrack.Isrc));
                    break;

                default: // SearchFailed — transient, so don't cache; it retries on the next run.
                    Log.TrackSearchDeferred(logger, sourceTrack.Name, sourceTrack.Artist);
                    break;
            }
        }

        if (!ctx.DryRun)
            await db.SaveChangesAsync(ct);

        if (ctx.DryRun && candidates.Count > 0)
            Log.DryRunWouldSaveMappings(logger, candidates.Count);

        jobRun.TracksMapped = candidates.Count;

        return candidates;
    }

    private static TrackMapping NewMapping(ImportRunContext ctx, string sourceId, string targetId, string? isrc) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            SourceProvider = ctx.SourceProviderName,
            SourceTrackId = sourceId,
            TargetProvider = ctx.TargetProviderName,
            TargetTrackId = targetId,
            Isrc = isrc ?? "",
            FirstMappedAt = DateTime.UtcNow
        };
}