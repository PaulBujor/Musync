using System.Text.Json.Serialization;

namespace Musync.Infrastructure.Spotify.Models;

public sealed class AlbumDto
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("artists")]
    public List<ArtistDto> Artists { get; init; } = [];
}
