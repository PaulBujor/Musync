using Musync.Domain.Interfaces;

namespace Musync.Jobs;

public record ImportRunContext(
    string SourceProviderName,
    string TargetProviderName,
    IMusicProvider Source,
    IMusicProvider Target,
    ITrackMapper Mapper,
    string PlaylistId,
    bool DryRun,
    int? Limit);
