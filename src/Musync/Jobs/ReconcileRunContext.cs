using Musync.Domain.Interfaces;

namespace Musync.Jobs;

public sealed record ReconcileRunContext(
    string ProviderName,
    IMusicProvider Target,
    string PlaylistId,
    bool DryRun);
