using System.Text.Json.Serialization;

namespace SpotifyTools.Infrastructure.Spotify.Models;

public class AlbumTrackItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("artists")]
    public List<ArtistDto> Artists { get; set; } = [];
}
