using System.Text.Json.Serialization;

namespace Musync.Infrastructure.Spotify.Models;

public class LikedTrackItem
{
    [JsonPropertyName("track")]
    public SpotifyTrackDto Track { get; set; } = null!;
}
