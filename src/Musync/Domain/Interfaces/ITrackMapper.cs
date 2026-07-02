using Musync.Domain;

namespace Musync.Domain.Interfaces;

public interface ITrackMapper
{
    Task<string?> FindTargetTrackIdAsync(Track track, CancellationToken ct);
}
