namespace Musync.Domain.Interfaces;

public interface ITrackMapper
{
    /// <summary>
    /// Maps a source track to a target-provider track. The result distinguishes a genuine
    /// no-match (cacheable) from a failed search (must not be cached, so it is retried).
    /// </summary>
    Task<TrackMatch> FindMatchAsync(Track track, CancellationToken ct);
}
