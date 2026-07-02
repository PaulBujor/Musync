using System.Text.Json.Serialization;

namespace Musync.Infrastructure.Spotify.Models;

public class PlaylistTrackItem
{
    [JsonPropertyName("item")]
    public SpotifyTrackDto? Item { get; set; }
}
