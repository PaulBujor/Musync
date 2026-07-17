using Musync.Domain;
using Musync.Domain.Interfaces;

namespace Musync.Tests.Fakes;

public sealed class LocalMockTrackMapper(
    Dictionary<string, string>? mapping = null,
    HashSet<string>? searchFailures = null) : ITrackMapper
{
    private readonly Dictionary<string, string> _mapping = mapping ?? new Dictionary<string, string>
    {
        { "tidal-1", "spotify-track-1" },
        { "tidal-2", "spotify-track-2" }
    };

    private readonly HashSet<string> _searchFailures = searchFailures ?? [];

    public int CallCount { get; private set; }

    public Task<TrackMatch> FindMatchAsync(Track track, CancellationToken ct)
    {
        CallCount++;

        if (_searchFailures.Contains(track.Id))
            return Task.FromResult(TrackMatch.SearchFailed);

        return Task.FromResult(_mapping.TryGetValue(track.Id, out var spotifyId)
            ? TrackMatch.Found(spotifyId)
            : TrackMatch.NotFound);
    }
}
