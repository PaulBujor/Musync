using System.Text.Json.Serialization;

namespace Musync.Infrastructure.Spotify.Models;

public sealed class PlaylistTrackItem
{
    [JsonPropertyName("item")]
    public SpotifyTrackDto? Item { get; init; }
}
