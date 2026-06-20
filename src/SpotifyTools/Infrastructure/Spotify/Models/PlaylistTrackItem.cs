using System.Text.Json.Serialization;

namespace SpotifyTools.Infrastructure.Spotify.Models;

public class PlaylistTrackItem
{
    [JsonPropertyName("item")]
    public SpotifyTrackDto? Item { get; set; }
}
