using System.Text.Json.Serialization;

namespace SpotifyTools.Infrastructure.Spotify.Models;

public class LikedTrackItem
{
    [JsonPropertyName("track")]
    public TrackRefDto Track { get; set; } = null!;
}

public class TrackRefDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";
}
