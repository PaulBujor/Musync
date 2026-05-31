using SpotifyTools.Domain;

namespace SpotifyTools.Domain.Interfaces;

public interface ITrackMapper
{
    Task<string?> FindSpotifyTrackIdAsync(Track track, CancellationToken ct);
}
