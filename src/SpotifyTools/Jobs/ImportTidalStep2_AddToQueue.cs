using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpotifyTools.Domain;
using SpotifyTools.Domain.Interfaces;
using SpotifyTools.Infrastructure.Persistence;
using SpotifyTools.Options;

namespace SpotifyTools.Jobs;

public sealed class ImportTidalStep2_AddToQueue(
    IMusicProvider spotifyMusic,
    SpotifyDbContext db,
    HybridCache cache,
    IOptions<SpotifyOptions> options,
    ILogger<ImportTidalStep2_AddToQueue> logger)
{
    private readonly SpotifyOptions _options = options.Value;

    public async Task ExecuteAsync(Domain.JobRun jobRun, List<(string SpotifyTrackId, Track TidalTrack)> candidates, CancellationToken ct)
    {
        Log.TidalStep2Start(logger);

        if (candidates.Count == 0)
        {
            logger.LogInformation("No mapped tracks to import.");
            return;
        }

        // Load existing track history to avoid duplicates
        var existingTrackIdsTask = db.TrackHistories
            .Select(x => x.SpotifyTrackId)
            .Distinct()
            .ToHashSetAsync(ct);

        // Also check current playlist contents
        var currentPlaylistIds = new HashSet<string>();
        await foreach (var track in spotifyMusic.GetPlaylistTracksAsync(_options.QueuePlaylistId, ct))
            currentPlaylistIds.Add(track.Id);

        // Skip tracks the user already likes on Spotify
        var likedTrackIds = await cache.GetOrCreateAsync(
            CacheKeys.LikedTracks,
            async ct2 =>
            {
                var ids = new HashSet<string>();
                await foreach (var t in spotifyMusic.GetSavedTracksAsync(ct2))
                    ids.Add(t.Id);
                return ids;
            },
            cancellationToken: ct);

        var existingTrackIds = await existingTrackIdsTask;

        var newTracks = new List<(string SpotifyTrackId, Track TidalTrack)>();
        foreach (var (spotifyId, tidalTrack) in candidates)
        {
            if (existingTrackIds.Contains(spotifyId) || currentPlaylistIds.Contains(spotifyId) || likedTrackIds.Contains(spotifyId))
            {
                jobRun.TracksSkipped++;
                continue;
            }
            newTracks.Add((spotifyId, tidalTrack));
        }

        if (newTracks.Count == 0)
        {
            logger.LogInformation("All mapped tracks are already in the queue.");
            return;
        }

        var trackUris = newTracks.Select(t => t.SpotifyTrackId);
        Log.AddingTracks(logger, newTracks.Count, _options.QueuePlaylistId);
        await spotifyMusic.AddTracksToPlaylistAsync(_options.QueuePlaylistId, trackUris, ct);

        var historyEntries = newTracks.Select(t => new TrackHistory
        {
            Id = Guid.CreateVersion7(),
            JobRunId = jobRun.Id,
            SpotifyTrackId = t.SpotifyTrackId,
            TrackName = t.TidalTrack.Name,
            ArtistName = t.TidalTrack.Artist,
            AlbumName = t.TidalTrack.Album,
            AddedAt = DateTime.UtcNow
        });
        db.TrackHistories.AddRange(historyEntries);
        await db.SaveChangesAsync(ct);

        jobRun.TracksAdded = newTracks.Count;

        // Re-read playlist for accurate final size
        var finalIds = new List<string>();
        await foreach (var track in spotifyMusic.GetPlaylistTracksAsync(_options.QueuePlaylistId, ct))
            finalIds.Add(track.Id);
        jobRun.QueueSizeAfter = finalIds.Count;

        db.JobRuns.Update(jobRun);
        await db.SaveChangesAsync(ct);
    }
}
