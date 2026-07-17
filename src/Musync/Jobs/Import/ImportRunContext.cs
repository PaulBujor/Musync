using Musync.Domain.Interfaces;

namespace Musync.Jobs.Import;

public sealed record ImportRunContext(
    string SourceProviderName,
    string TargetProviderName,
    IMusicProvider Source,
    IMusicProvider Target,
    ITrackMapper Mapper,
    string PlaylistId,
    bool DryRun,
    int? Limit);
