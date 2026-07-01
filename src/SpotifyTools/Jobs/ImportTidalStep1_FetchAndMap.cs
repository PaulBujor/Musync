using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SpotifyTools.Domain;
using SpotifyTools.Domain.Interfaces;
using SpotifyTools.Infrastructure.Persistence;

namespace SpotifyTools.Jobs;

public sealed class ImportTidalStep1_FetchAndMap(
    [FromKeyedServices("tidal")] IMusicProvider tidalMusic,
    ITrackMapper trackMapper,
    AppDbContext db,
    ILogger<ImportTidalStep1_FetchAndMap> logger)
{
    public async Task<List<(string SpotifyTrackId, Track TidalTrack)>> ExecuteAsync(Domain.JobRun jobRun, CancellationToken ct)
    {
        Log.TidalStep1Start(logger);

        var existingMappings = await db.TidalTrackMappings
            .ToDictionaryAsync(m => m.TidalTrackId, m => m.SpotifyTrackId, ct);

        var candidates = new List<(string SpotifyTrackId, Track TidalTrack)>();

        await foreach (var tidalTrack in tidalMusic.GetSavedTracksAsync(ct))
        {
            var tidalId = tidalTrack.Id;

            if (existingMappings.TryGetValue(tidalId, out var cachedSpotifyId))
            {
                if (!string.IsNullOrEmpty(cachedSpotifyId))
                    candidates.Add((cachedSpotifyId, tidalTrack));
                continue;
            }

            var spotifyId = await trackMapper.FindTargetTrackIdAsync(tidalTrack, ct);

            db.TidalTrackMappings.Add(new TidalTrackMapping
            {
                Id = Guid.CreateVersion7(),
                TidalTrackId = tidalId,
                SpotifyTrackId = spotifyId ?? "",
                Isrc = tidalTrack.Isrc ?? "",
                FirstMappedAt = DateTime.UtcNow
            });

            if (spotifyId is not null)
                candidates.Add((spotifyId, tidalTrack));
            else
                Log.TidalTrackNotMapped(logger, tidalTrack.Name, tidalTrack.Artist);
        }

        await db.SaveChangesAsync(ct);
        jobRun.NewAlbumsEncountered = candidates.Count;

        return candidates;
    }
}
