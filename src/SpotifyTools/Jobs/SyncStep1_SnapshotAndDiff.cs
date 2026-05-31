using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SpotifyTools.Domain.Interfaces;
using SpotifyTools.Options;

namespace SpotifyTools.Jobs;

public sealed class SyncStep1_SnapshotAndDiff
{
    private readonly IMusicProvider _music;
    private readonly ITrackHistoryRepository _historyRepo;
    private readonly IJobRunRepository _jobRunRepo;
    private readonly HybridCache _cache;
    private readonly SpotifyOptions _options;
    private readonly ILogger<SyncStep1_SnapshotAndDiff> _logger;

    public SyncStep1_SnapshotAndDiff(
        IMusicProvider music,
        ITrackHistoryRepository historyRepo,
        IJobRunRepository jobRunRepo,
        HybridCache cache,
        SpotifyOptions options,
        ILogger<SyncStep1_SnapshotAndDiff> logger)
    {
        _music = music;
        _historyRepo = historyRepo;
        _jobRunRepo = jobRunRepo;
        _cache = cache;
        _options = options;
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
            foreach (var id in likedInPlaylist)
                await _historyRepo.MarkRemovedAsync(id, "liked", DateTime.UtcNow, ct);
            jobRun.TracksRemovedLiked = likedInPlaylist.Count;
        }

        var activeHistory = await _historyRepo.GetActiveHistoryAsync(ct);
        var manualRemovals = activeHistory
            .Where(h => !currentTrackIds.Contains(h.SpotifyTrackId) && !likedInPlaylist.Contains(h.SpotifyTrackId))
            .ToList();

        if (manualRemovals.Count > 0)
        {
            Log.MarkingManualRemovals(_logger, manualRemovals.Count);
            foreach (var entry in manualRemovals)
                await _historyRepo.MarkRemovedAsync(entry.SpotifyTrackId, "manual", DateTime.UtcNow, ct);
            jobRun.TracksRemovedManual = manualRemovals.Count;
        }

        jobRun.QueueSizeAfter = currentTrackIds.Count - likedInPlaylist.Count;
        await _jobRunRepo.UpdateAsync(jobRun, ct);
    }
}
