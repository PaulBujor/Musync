using SpotifyTools.Domain;
using SpotifyTools.Domain.Interfaces;

namespace SpotifyTools.Tests.Fakes;

public sealed class LocalMockTrackMapper(Dictionary<string, string>? mapping = null) : ITrackMapper
{
    private readonly Dictionary<string, string> _mapping = mapping ?? new()
    {
        { "tidal-1", "spotify-track-1" },
        { "tidal-2", "spotify-track-2" },
    };

    public Task<string?> FindSpotifyTrackIdAsync(Track track, CancellationToken ct)
    {
        var found = _mapping.TryGetValue(track.Id, out var spotifyId);
        return Task.FromResult(found ? spotifyId : null);
    }
}
