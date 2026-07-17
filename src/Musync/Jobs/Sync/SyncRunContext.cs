using Musync.Domain.Interfaces;

namespace Musync.Jobs.Sync;

public sealed record SyncRunContext(
    string ProviderName,
    IMusicProvider Target,
    string PlaylistId,
    int MaxDegreeOfParallelism,
    bool DryRun,
    int? Limit);