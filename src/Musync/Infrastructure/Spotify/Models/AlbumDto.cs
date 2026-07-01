using System.Text.Json.Serialization;

namespace Musync.Infrastructure.Spotify.Models;

public class AlbumDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("artists")]
    public List<ArtistDto> Artists { get; set; } = [];
}
