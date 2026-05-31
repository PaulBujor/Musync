using Microsoft.Extensions.Logging;

namespace SpotifyTools.Jobs;

public static partial class Log
{
    [LoggerMessage(Level = LogLevel.Information, Message = "=== Spotify Queue Sync Complete ===")]
    public static partial void SyncCompleteHeader(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Duration:          {Duration}")]
    public static partial void SyncDuration(ILogger logger, string duration);

    [LoggerMessage(Level = LogLevel.Information, Message = "Status:            {Status}")]
    public static partial void SyncStatus(ILogger logger, string status);

    [LoggerMessage(Level = LogLevel.Information, Message = "Tracks added:      {Count}")]
    public static partial void TracksAdded(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Tracks removed:    {Total} ({Liked} liked, {Manual} manual)")]
    public static partial void TracksRemoved(ILogger logger, int total, int liked, int manual);

    [LoggerMessage(Level = LogLevel.Information, Message = "Tracks skipped:    {Count}")]
    public static partial void TracksSkipped(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "New albums seen:   {Count}")]
    public static partial void NewAlbumsSeen(ILogger logger, int count);

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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rate limited by Spotify. Retrying after {Delay}")]
    public static partial void RateLimited(ILogger logger, TimeSpan delay);

    [LoggerMessage(Level = LogLevel.Error, Message = "Job run failed: {Message}")]
    public static partial void JobFailed(ILogger logger, string message, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting queue sync job {JobRunId}")]
    public static partial void StartingJob(ILogger logger, string jobRunId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Step 1: Snapshot & diff (liked + manual removal)")]
    public static partial void Step1Start(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Step 2: Add new tracks from saved albums")]
    public static partial void Step2Start(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Processing album {AlbumName} by {ArtistName}")]
    public static partial void ProcessingAlbum(ILogger logger, string albumName, string artistName);
}
