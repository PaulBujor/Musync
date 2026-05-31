using System.Text.Json.Serialization;

namespace SpotifyTools.Infrastructure.Spotify.Models;

public class ArtistDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}
