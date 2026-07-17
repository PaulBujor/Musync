using System.ComponentModel.DataAnnotations;

namespace Musync.Options;

public sealed record SpotifyOptions : ProviderOptionsBase
{
    public string QueuePlaylistId { get; init; } = "";
    [Range(0, 5000)] public int RequestDelayMs { get; init; } = 100;
    [Range(1, 10)] public int MaxConcurrentRequests { get; init; } = 3;
}
