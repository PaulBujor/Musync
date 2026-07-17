using Musync.Domain;
using Musync.Domain.Interfaces;

namespace Musync.Tests.Fakes;

public sealed class LocalMockTrackMapper(Dictionary<string, string>? mapping = null) : ITrackMapper
{
    private readonly Dictionary<string, string> _mapping = mapping ?? new Dictionary<string, string>
    {
        { "tidal-1", "spotify-track-1" },
        { "tidal-2", "spotify-track-2" }
    };

    public int CallCount { get; private set; }

    public Task<string?> FindTargetTrackIdAsync(Track track, CancellationToken ct)
    {
        CallCount++;
        var found = _mapping.TryGetValue(track.Id, out var spotifyId);
        return Task.FromResult(found ? spotifyId : null);
    }
}