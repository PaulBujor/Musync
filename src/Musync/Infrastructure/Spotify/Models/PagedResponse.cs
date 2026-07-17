using System.Text.Json.Serialization;

namespace Musync.Infrastructure.Spotify.Models;

public sealed class PagedResponse<T>
{
    [JsonPropertyName("items")] public List<T> Items { get; init; } = [];

    [JsonPropertyName("next")] public string? Next { get; init; }
}