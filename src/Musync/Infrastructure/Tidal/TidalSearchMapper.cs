using Musync.Domain;
using Musync.Domain.Interfaces;

namespace Musync.Infrastructure.Tidal;

public sealed class TidalSearchMapper : ITrackMapper
{
    public Task<string?> FindTargetTrackIdAsync(Track track, CancellationToken ct)
        => throw new NotSupportedException("Tidal track search is not yet supported.");
}
