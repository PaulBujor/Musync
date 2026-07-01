using System.Text.Json.Serialization;

namespace Musync.Infrastructure.Spotify.Models;

public class PagedResponse<T>
{
    [JsonPropertyName("items")]
    public List<T> Items { get; set; } = [];

    [JsonPropertyName("next")]
    public string? Next { get; set; }
}
