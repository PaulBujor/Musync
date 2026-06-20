using System.Text.Json.Serialization;

namespace SpotifyTools.Infrastructure.Spotify.Models;

public class SpotifyTrackDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("artists")]
    public List<ArtistDto> Artists { get; set; } = [];

    [JsonPropertyName("album")]
    public AlbumDto? Album { get; set; }

    [JsonPropertyName("external_ids")]
    public ExternalIdsDto? ExternalIds { get; set; }
}

public class ExternalIdsDto
{
    [JsonPropertyName("isrc")]
    public string? Isrc { get; set; }
}
