using System.Text.Json.Serialization;

namespace Musync.Infrastructure.Spotify.Models;

public sealed class SpotifyTrackDto
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("artists")]
    public List<ArtistDto> Artists { get; init; } = [];

    [JsonPropertyName("album")]
    public AlbumDto? Album { get; init; }

    [JsonPropertyName("external_ids")]
    public ExternalIdsDto? ExternalIds { get; init; }
}

public sealed class ExternalIdsDto
{
    [JsonPropertyName("isrc")]
    public string? Isrc { get; init; }
}
