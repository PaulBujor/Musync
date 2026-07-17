using Musync.Domain.Interfaces;

namespace Musync.Jobs;

public record SyncRunContext(
    string ProviderName,
    IMusicProvider Target,
    string PlaylistId,
    int MaxDegreeOfParallelism,
    bool DryRun,
    int? Limit)
{
    public int QueueSizeAfterStep1 { get; set; }

    // Track ids currently in the playlist, captured by step 1 so step 2 can skip re-adding them.
    public HashSet<string> CurrentPlaylistTrackIds { get; set; } = [];
}
