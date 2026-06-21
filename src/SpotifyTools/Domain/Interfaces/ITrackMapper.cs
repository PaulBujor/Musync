using SpotifyTools.Domain;

namespace SpotifyTools.Domain.Interfaces;

public interface ITrackMapper
{
    Task<string?> FindTargetTrackIdAsync(Track track, CancellationToken ct);
}
