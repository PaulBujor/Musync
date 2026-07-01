using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpotifyTools.Domain;
using SpotifyTools.Domain.Interfaces;
using SpotifyTools.Infrastructure.Persistence;
using SpotifyTools.Options;

namespace SpotifyTools.Jobs;

public sealed class SyncStep2_AddNewTracks(
    IMusicProvider music,
    AppDbContext db,
    HybridCache cache,
    IOptions<SpotifyOptions> options,
    ILogger<SyncStep2_AddNewTracks> logger)
{
    private readonly SpotifyOptions _options = options.Value;

    public async Task ExecuteAsync(Domain.JobRun jobRun, CancellationToken ct)
    {
        Log.Step2Start(logger);

        var likedTrackIdsTask = cache.GetOrCreateAsync(
            CacheKeys.LikedTracks,
            async ct2 =>
            {
                var ids = new HashSet<string>();
                await foreach (var t in music.GetSavedTracksAsync(ct2))
                    ids.Add(t.Id);
                return ids;
            },
            cancellationToken: ct).AsTask();

        var historyTrackIdsTask = db.TrackHistories
            .Select(x => x.SpotifyTrackId)
            .Distinct()
            .ToHashSetAsync(ct);

        var processedAlbumIdsTask = db.ProcessedAlbums
            .Select(a => a.SpotifyAlbumId)
            .ToHashSetAsync(ct);

        var likedTrackIds = await likedTrackIdsTask;
        var historyTrackIds = await historyTrackIdsTask;
        var processedAlbumIds = await processedAlbumIdsTask;

        var newTracks = new List<Domain.Track>();
        var newlyProcessedAlbums = new List<ProcessedAlbum>();
        var albumsProcessed = 0;
        var tracksSkipped = 0;
        var lockObj = new object();

        await Parallel.ForEachAsync(
            music.GetSavedAlbumsAsync(ct),
            new ParallelOptions { MaxDegreeOfParallelism = _options.MaxConcurrentRequests, CancellationToken = ct },
            async (album, ct2) =>
            {
                if (processedAlbumIds.Contains(album.Id))
                    return;

                Log.ProcessingAlbum(logger, album.Name, album.Artist);

                var albumTracks = new List<Domain.Track>();
                await foreach (var track in music.GetAlbumTracksAsync(album.Id, album.Name, ct2))
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
                    newTracks.AddRange(albumTracks);
                    albumsProcessed++;

                    newlyProcessedAlbums.Add(new ProcessedAlbum
                    {
                        Id = Guid.CreateVersion7(),
                        SpotifyAlbumId = album.Id,
                        AlbumName = album.Name,
                        ArtistName = album.Artist,
                        FirstProcessedAt = DateTime.UtcNow,
                        LastSeenAt = DateTime.UtcNow
                    });
                }
            });

        jobRun.TracksSkipped = tracksSkipped;

        if (newTracks.Count > 0)
        {
            var trackUris = newTracks.Select(t => t.Id);
            Log.AddingTracks(logger, newTracks.Count, _options.QueuePlaylistId);
            await music.AddTracksToPlaylistAsync(_options.QueuePlaylistId, trackUris, ct);

            var historyEntries = newTracks.Select(t => new TrackHistory
            {
                Id = Guid.CreateVersion7(),
                JobRunId = jobRun.Id,
                SpotifyTrackId = t.Id,
                TrackName = t.Name,
                ArtistName = t.Artist,
                AlbumName = t.Album,
                AddedAt = DateTime.UtcNow
            });

            db.TrackHistories.AddRange(historyEntries);
            await db.SaveChangesAsync(ct);
            jobRun.TracksAdded = newTracks.Count;
        }

        jobRun.NewAlbumsEncountered = albumsProcessed;

        if (newlyProcessedAlbums.Count > 0)
        {
            db.ProcessedAlbums.AddRange(newlyProcessedAlbums);
            await db.SaveChangesAsync(ct);
        }

        var currentPlaylistTrackIds = new List<string>();
        await foreach (var track in music.GetPlaylistTracksAsync(_options.QueuePlaylistId, ct))
            currentPlaylistTrackIds.Add(track.Id);
        jobRun.QueueSizeAfter = currentPlaylistTrackIds.Count;
        db.JobRuns.Update(jobRun);
        await db.SaveChangesAsync(ct);
    }
}
