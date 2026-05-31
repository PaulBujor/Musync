using System.ComponentModel.DataAnnotations;

namespace SpotifyTools.Options;

public sealed record SpotifyOptions
{
    [Required] public string ClientId { get; init; } = "";
    [Required] public string ClientSecret { get; init; } = "";
    [Required] public string QueuePlaylistId { get; init; } = "";
    [Range(0, 5000)] public int RequestDelayMs { get; init; } = 100;
    [Range(1, 10)] public int MaxRetries { get; init; } = 3;
    [Range(1, 10)] public int MaxConcurrentRequests { get; init; } = 3;
}
