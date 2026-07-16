using System.ComponentModel.DataAnnotations;

namespace Musync.Options;

public sealed record TidalOptions : ProviderOptionsBase
{
    [Required] public string QueuePlaylistId { get; init; } = "";
    [Range(1, 10)] public int MaxConcurrentRequests { get; init; } = 3;
}
