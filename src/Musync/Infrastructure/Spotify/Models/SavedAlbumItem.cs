using System.Text.Json.Serialization;

namespace Musync.Infrastructure.Spotify.Models;

public sealed class SavedAlbumItem
{
    [JsonPropertyName("album")]
    public AlbumDto Album { get; init; } = null!;
}
