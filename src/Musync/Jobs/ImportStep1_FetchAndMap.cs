using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Musync.Domain;
using Musync.Infrastructure.Persistence;

namespace Musync.Jobs;

public sealed class ImportStep1_FetchAndMap(
    AppDbContext db,
    ILogger<ImportStep1_FetchAndMap> logger)
{
    public async Task<List<(string TargetTrackId, Track SourceTrack)>> ExecuteAsync(JobRun jobRun, ImportRunContext ctx, CancellationToken ct)
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
                    Log.TrackNotMapped(logger, sourceTrack.Name, sourceTrack.Artist);
                }
            }
        }

        if (!ctx.DryRun)
            await db.SaveChangesAsync(ct);

        if (ctx.DryRun && candidates.Count > 0)
            Log.DryRunWouldSaveMappings(logger, candidates.Count);

        jobRun.NewAlbumsEncountered = candidates.Count;

        return candidates;
    }
}
