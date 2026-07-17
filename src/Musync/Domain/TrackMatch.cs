namespace Musync.Domain;

/// <summary>Outcome of mapping a source track to a target-provider track.</summary>
public enum TrackMatchOutcome
{
    /// <summary>A confident target match was found.</summary>
    Matched,

    /// <summary>The search completed but found no acceptable match — safe to cache as a negative.</summary>
    NotFound,

    /// <summary>The search itself failed (e.g. a transient HTTP error). Do NOT cache; retry next run.</summary>
    SearchFailed
}

public readonly record struct TrackMatch(TrackMatchOutcome Outcome, string? TargetTrackId)
{
    public static readonly TrackMatch NotFound = new(TrackMatchOutcome.NotFound, null);
    public static readonly TrackMatch SearchFailed = new(TrackMatchOutcome.SearchFailed, null);

    public static TrackMatch Found(string targetTrackId) => new(TrackMatchOutcome.Matched, targetTrackId);
}
