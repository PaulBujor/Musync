using System.Text.Json.Serialization;

namespace Musync.Infrastructure.Spotify.Models;

public sealed class LikedTrackItem
{
    [JsonPropertyName("track")]
    public SpotifyTrackDto Track { get; init; } = null!;
}
