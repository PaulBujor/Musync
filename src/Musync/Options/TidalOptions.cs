using System.ComponentModel.DataAnnotations;

namespace Musync.Options;

public sealed record TidalOptions : ProviderOptionsBase
{
    // Sent as the `locale` query parameter on v2 collection/catalog reads (e.g. "en-US").
    public string Locale { get; init; } = "en-US";

    // The playlist `queue-albums` syncs saved-album tracks into. Required for that command.
    public string QueuePlaylistId { get; init; } = "";

    // Parallel album-track reads during queue-albums (mirrors Spotify).
    [Range(1, 10)] public int MaxConcurrentRequests { get; init; } = 3;
}
