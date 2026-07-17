using System.Text.Json.Serialization;

namespace Musync.Infrastructure.Spotify.Models;

public sealed class ArtistDto
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";
}
