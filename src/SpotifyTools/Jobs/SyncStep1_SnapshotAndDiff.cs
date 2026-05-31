using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpotifyTools.Domain.Interfaces;
using SpotifyTools.Infrastructure.Persistence;
using SpotifyTools.Options;

namespace SpotifyTools.Jobs;

public sealed class SyncStep1_SnapshotAndDiff
{
    private readonly IMusicProvider _music;
    private readonly SpotifyDbContext _db;
    private readonly HybridCache _cache;
    private readonly SpotifyOptions _options;
    private readonly ILogger<SyncStep1_SnapshotAndDiff> _logger;

    public SyncStep1_SnapshotAndDiff(
        IMusicProvider music,
        SpotifyDbContext db,
        HybridCache cache,
        IOptions<SpotifyOptions> options,
        ILogger<SyncStep1_SnapshotAndDiff> logger)
    {
        _music = music;
        _db = db;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    public async Task ExecuteAsync(Domain.JobRun jobRun, CancellationToken ct)
    {
        Log.Step1Start(_logger);

        var currentPlaylistTracks = await _cache.GetOrCreateAsync(
            CacheKeys.QueuePlaylist,
            async ct2 =>
            {
                var tracks = new List<Domain.Track>();
                await foreach (var track in _music.GetPlaylistTracksAsync(_options.QueuePlaylistId, ct2))
                    tracks.Add(track);
                return tracks;
            },
            cancellationToken: ct);

        var likedTrackIds = await _cache.GetOrCreateAsync(
            CacheKeys.LikedTracks,
            async ct2 => await _music.GetLikedTrackIdsAsync(ct2),
            cancellationToken: ct);

        var currentTrackIds = currentPlaylistTracks.Select(t => t.Id).ToHashSet();

        var likedInPlaylist = currentTrackIds.Where(id => likedTrackIds.Contains(id)).ToList();
        if (likedInPlaylist.Count > 0)
        {
            Log.RemovingLikedTracks(_logger, likedInPlaylist.Count);
            await _music.RemoveTracksFromPlaylistAsync(_options.QueuePlaylistId, likedInPlaylist, ct);

            var now = DateTime.UtcNow;
            foreach (var id in likedInPlaylist)
            {
                var entries = await _db.TrackHistories
                    .Where(x => x.SpotifyTrackId == id && x.RemovedAt == null)
                    .ToListAsync(ct);
                foreach (var entry in entries)
                {
                    entry.RemovedAt = now;
                    entry.RemovalReason = "liked";
                }
            }
            await _db.SaveChangesAsync(ct);

            jobRun.TracksRemovedLiked = likedInPlaylist.Count;
        }

        var activeHistory = await _db.TrackHistories.Where(x => x.RemovedAt == null).ToListAsync(ct);
        var manualRemovals = activeHistory
            .Where(h => !currentTrackIds.Contains(h.SpotifyTrackId) && !likedInPlaylist.Contains(h.SpotifyTrackId))
            .ToList();

        if (manualRemovals.Count > 0)
        {
            Log.MarkingManualRemovals(_logger, manualRemovals.Count);
            var now = DateTime.UtcNow;
            foreach (var entry in manualRemovals)
            {
                entry.RemovedAt = now;
                entry.RemovalReason = "manual";
            }
            await _db.SaveChangesAsync(ct);
            jobRun.TracksRemovedManual = manualRemovals.Count;
        }

        jobRun.QueueSizeAfter = currentTrackIds.Count - likedInPlaylist.Count;
        _db.JobRuns.Update(jobRun);
        await _db.SaveChangesAsync(ct);
    }
}
