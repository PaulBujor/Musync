using Microsoft.Extensions.Logging;

namespace Musync.Jobs;

public static partial class Log
{
    [LoggerMessage(Level = LogLevel.Information, Message = "=== Sync Complete ===")]
    public static partial void SyncCompleteHeader(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Duration:          {Duration}")]
    public static partial void SyncDuration(ILogger logger, string duration);

    [LoggerMessage(Level = LogLevel.Information, Message = "Status:            {Status}")]
    public static partial void SyncStatus(ILogger logger, string status);

    [LoggerMessage(Level = LogLevel.Information, Message = "Tracks added:      {Count}")]
    public static partial void TracksAdded(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Tracks removed:    {Total} ({Liked} liked, {Manual} manual)")]
    public static partial void TracksRemoved(ILogger logger, int total, int liked, int manual);

    [LoggerMessage(Level = LogLevel.Information, Message = "Tracks skipped:    {Count}")]
    public static partial void TracksSkipped(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "New albums seen:   {Count}")]
    public static partial void NewAlbumsSeen(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Albums skipped:    {Count}")]
    public static partial void AlbumsSkipped(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "  - {AlbumName} by {ArtistName} ({Reason})")]
    public static partial void AlbumSkippedDetail(ILogger logger, string albumName, string artistName, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Queue size:        {Count}")]
    public static partial void QueueSize(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "===================================")]
    public static partial void SyncCompleteFooter(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Adding {TrackCount} tracks to playlist {PlaylistId}")]
    public static partial void AddingTracks(ILogger logger, int trackCount, string playlistId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Removing {TrackCount} liked tracks from playlist")]
    public static partial void RemovingLikedTracks(ILogger logger, int trackCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Marking {TrackCount} tracks as manually removed")]
    public static partial void MarkingManualRemovals(ILogger logger, int trackCount);

    [LoggerMessage(Level = LogLevel.Error, Message = "Job run failed: {Message}")]
    public static partial void JobFailed(ILogger logger, string message, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting {Command} job for {Provider} {JobRunId}")]
    public static partial void StartingJob(ILogger logger, string command, string provider, string jobRunId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Step 1: Snapshot & diff (liked + manual removal)")]
    public static partial void Step1Start(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Step 2: Add new tracks from saved albums")]
    public static partial void Step2Start(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Processing album {AlbumName} by {ArtistName}")]
    public static partial void ProcessingAlbum(ILogger logger, string albumName, string artistName);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Album \"{AlbumName}\" by {ArtistName} is unavailable (404); skipping")]
    public static partial void AlbumTracksUnavailable(ILogger logger, string albumName, string artistName);

    // Import logs
    [LoggerMessage(Level = LogLevel.Information, Message = "=== Import Complete ===")]
    public static partial void ImportCompleteHeader(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Tracks mapped: {Count}")]
    public static partial void TracksMapped(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Step 1: Fetch source favorites & map to target tracks")]
    public static partial void ImportStep1Start(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Step 2: Add mapped tracks to queue playlist")]
    public static partial void ImportStep2Start(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Could not find target match for source track \"{TrackName}\" by {ArtistName}")]
    public static partial void TrackNotMapped(ILogger logger, string trackName, string artistName);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Search failed for source track \"{TrackName}\" by {ArtistName}; will retry next run")]
    public static partial void TrackSearchDeferred(ILogger logger, string trackName, string artistName);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "{Provider} rate limited (HTTP 429) on attempt {Attempt}; waiting {DelaySeconds:F0}s before retrying (Ctrl+C to cancel)")]
    public static partial void RateLimited(ILogger logger, string provider, int attempt, double delaySeconds);

    [LoggerMessage(Level = LogLevel.Information, Message = "No mapped tracks to import")]
    public static partial void NoMappedTracksToImport(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "All mapped tracks are already in the queue")]
    public static partial void AllMappedTracksAlreadyInQueue(ILogger logger);

    // Dry-run logs
    [LoggerMessage(Level = LogLevel.Information, Message = "[DRY-RUN] Would remove {Count} liked tracks from playlist")]
    public static partial void DryRunWouldRemoveLiked(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "[DRY-RUN] Would add {Count} tracks to playlist {PlaylistId}")]
    public static partial void DryRunWouldAdd(ILogger logger, int count, string playlistId);

    [LoggerMessage(Level = LogLevel.Information, Message = "[DRY-RUN] Would mark {Count} manual removals")]
    public static partial void DryRunWouldMarkManualRemovals(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "[DRY-RUN] Would save {Count} track history entries")]
    public static partial void DryRunWouldSaveHistory(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "[DRY-RUN] Would save {Count} processed albums")]
    public static partial void DryRunWouldSaveAlbums(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "[DRY-RUN] Would save {Count} track mappings")]
    public static partial void DryRunWouldSaveMappings(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "DRY-RUN mode active \u2014 no mutations will be made")]
    public static partial void DryRunActive(ILogger logger);

    // Limit logs
    [LoggerMessage(Level = LogLevel.Information, Message = "[LIMIT] Reached limit of {Limit} items, stopping early")]
    public static partial void LimitReached(ILogger logger, int limit);

    [LoggerMessage(Level = LogLevel.Information, Message = "[LIMIT] Run was capped at {Limit} items")]
    public static partial void LimitApplied(ILogger logger, int limit);

    // Deprecated command
    [LoggerMessage(Level = LogLevel.Warning, Message = "Command '{Old}' is deprecated. Use '{New}' instead.")]
    public static partial void DeprecatedCommand(ILogger logger, string old, string @new);

    // Reconcile
    [LoggerMessage(Level = LogLevel.Information, Message = "Reconciling queue playlist {PlaylistId}")]
    public static partial void ReconcileStart(ILogger logger, string playlistId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Found {DuplicateTracks} duplicated tracks ({ExtraCopies} extra copies) in the playlist")]
    public static partial void ReconcileFoundDuplicates(ILogger logger, int duplicateTracks, int extraCopies);

    [LoggerMessage(Level = LogLevel.Information, Message = "[DRY-RUN] Would remove {Count} duplicate copies")]
    public static partial void DryRunWouldRemoveDuplicates(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Backfilled {Count} track history entries")]
    public static partial void ReconcileBackfilledHistory(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Queue already has no duplicates")]
    public static partial void ReconcileNoDuplicates(ILogger logger);

    // Reconcile end-of-run report
    [LoggerMessage(Level = LogLevel.Information, Message = "=== Reconcile Complete ===")]
    public static partial void ReconcileCompleteHeader(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Playlist items:      {Count}")]
    public static partial void ReconcilePlaylistItems(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Distinct songs:      {Count}")]
    public static partial void ReconcileDistinctSongs(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Duplicate groups:    {Count}")]
    public static partial void ReconcileDuplicateGroups(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Copies removed:      {Count}")]
    public static partial void ReconcileCopiesRemoved(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Copies to remove:    {Count}")]
    public static partial void ReconcileCopiesToRemove(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "History backfilled:  {Count}")]
    public static partial void ReconcileHistoryBackfilled(ILogger logger, int count);
}