using System.ComponentModel.DataAnnotations;

namespace Musync.Options;

public sealed record TidalOptions : ProviderOptionsBase
{
    [Required] public string QueuePlaylistId { get; init; } = "";
}
