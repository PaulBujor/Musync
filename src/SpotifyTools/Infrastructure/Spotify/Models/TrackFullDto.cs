using System.Text.Json.Serialization;

namespace SpotifyTools.Infrastructure.Spotify.Models;

public class TrackFullDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("artists")]
    public List<ArtistDto> Artists { get; set; } = [];

    [JsonPropertyName("album")]
    public AlbumDto Album { get; set; } = null!;
}
