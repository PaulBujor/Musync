using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpotifyTools.Domain;
using SpotifyTools.Domain.Interfaces;
using SpotifyTools.Infrastructure.Persistence;
using SpotifyTools.Options;

namespace SpotifyTools.Jobs;

public sealed class SyncStep2_AddNewTracks
{
    private readonly HybridCache _cache;
    private readonly SpotifyDbContext _db;
    private readonly ITrackHistoryRepository _historyRepo;
    private readonly IJobRunRepository _jobRunRepo;
    private readonly ILogger<SyncStep2_AddNewTracks> _logger;
    private readonly IMusicProvider _music;
    private readonly SpotifyOptions _options;

    public SyncStep2_AddNewTracks(
        IMusicProvider music,
        ITrackHistoryRepository historyRepo,
        IJobRunRepository jobRunRepo,
        SpotifyDbContext db,
        HybridCache cache,
        IOptions<SpotifyOptions> options,
        ILogger<SyncStep2_AddNewTracks> logger)
    {
        _music = music;
        _historyRepo = historyRepo;
        _jobRunRepo = jobRunRepo;
        _db = db;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    public async Task ExecuteAsync(JobRun jobRun, CancellationToken ct)
    {
        Log.Step2Start(_logger);

        var likedTrackIds = await _cache.GetOrCreateAsync(
            CacheKeys.LikedTracks,
            async ct2 => await _music.GetLikedTrackIdsAsync(ct2),
            cancellationToken: ct);

        var historyTrackIds = await _cache.GetOrCreateAsync(
            CacheKeys.TrackHistoryExists,
            async ct2 => await _historyRepo.GetTrackIdSetAsync(ct2),
            cancellationToken: ct);

        var processedAlbumIds = await _db.ProcessedAlbums
            .Select(a => a.SpotifyAlbumId)
            .ToHashSetAsync(ct);

        var newTracks = new List<Track>();
        var newlyProcessedAlbums = new List<ProcessedAlbum>();
        var albumsProcessed = 0;
        var tracksSkipped = 0;
        var lockObj = new object();

        await Parallel.ForEachAsync(
            _music.GetSavedAlbumsAsync(ct),
            new ParallelOptions { MaxDegreeOfParallelism = _options.MaxConcurrentRequests, CancellationToken = ct },
            async (album, ct2) =>
            {
                if (processedAlbumIds.Contains(album.Id))
                    return;

                Log.ProcessingAlbum(_logger, album.Name, album.Artist);

                var albumTracks = new List<Track>();
                await foreach (var track in _music.GetAlbumTracksAsync(album.Id, album.Name, ct2))
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
            Log.AddingTracks(_logger, newTracks.Count, _options.QueuePlaylistId);
            await _music.AddTracksToPlaylistAsync(_options.QueuePlaylistId, trackUris, ct);

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

            await _historyRepo.AddTrackHistoryAsync(historyEntries, ct);
            jobRun.TracksAdded = newTracks.Count;
        }

        jobRun.NewAlbumsEncountered = albumsProcessed;

        if (newlyProcessedAlbums.Count > 0)
        {
            _db.ProcessedAlbums.AddRange(newlyProcessedAlbums);
            await _db.SaveChangesAsync(ct);
        }

        var currentPlaylistTrackIds = await _cache.GetOrCreateAsync(
            CacheKeys.QueuePlaylist,
            async ct2 =>
            {
                var ids = new List<string>();
                await foreach (var track in _music.GetPlaylistTracksAsync(_options.QueuePlaylistId, ct2))
                    ids.Add(track.Id);
                return ids;
            },
            cancellationToken: ct);

        jobRun.QueueSizeAfter = currentPlaylistTrackIds.Count;
        await _jobRunRepo.UpdateAsync(jobRun, ct);
    }
}