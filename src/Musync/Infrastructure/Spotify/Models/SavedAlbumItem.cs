using System.Text.Json.Serialization;

namespace Musync.Infrastructure.Spotify.Models;

public class SavedAlbumItem
{
    [JsonPropertyName("album")]
    public AlbumDto Album { get; set; } = null!;
}
