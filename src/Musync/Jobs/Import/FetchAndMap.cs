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

            var targetId = await ctx.Mapper.FindTargetTrackIdAsync(sourceTrack, ct);

            if (ctx.DryRun)
            {
                if (targetId is not null)
                    candidates.Add((targetId, sourceTrack));
                else
                    Log.TrackNotMapped(logger, sourceTrack.Name, sourceTrack.Artist);
            }
            else
            {
                if (targetId is not null)
                {
                    db.TrackMappings.Add(new TrackMapping
                    {
                        Id = Guid.CreateVersion7(),
                        SourceProvider = ctx.SourceProviderName,
                        SourceTrackId = sourceId,
                        TargetProvider = ctx.TargetProviderName,
                        TargetTrackId = targetId,
                        Isrc = sourceTrack.Isrc ?? "",
                        FirstMappedAt = DateTime.UtcNow
                    });
                    candidates.Add((targetId, sourceTrack));
                }
                else
                {
                    // Persist a negative mapping (empty target) so we don't re-search it next run.
                    db.TrackMappings.Add(new TrackMapping
                    {
                        Id = Guid.CreateVersion7(),
                        SourceProvider = ctx.SourceProviderName,
                        SourceTrackId = sourceId,
                        TargetProvider = ctx.TargetProviderName,
                        TargetTrackId = "",
                        Isrc = sourceTrack.Isrc ?? "",
                        FirstMappedAt = DateTime.UtcNow
                    });
                    Log.TrackNotMapped(logger, sourceTrack.Name, sourceTrack.Artist);
                }
            }
        }

        if (!ctx.DryRun)
            await db.SaveChangesAsync(ct);

        if (ctx.DryRun && candidates.Count > 0)
            Log.DryRunWouldSaveMappings(logger, candidates.Count);

        jobRun.TracksMapped = candidates.Count;

        return candidates;
    }
}