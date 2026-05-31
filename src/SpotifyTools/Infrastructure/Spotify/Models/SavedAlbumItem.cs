using System.Text.Json.Serialization;

namespace SpotifyTools.Infrastructure.Spotify.Models;

public class SavedAlbumItem
{
    [JsonPropertyName("album")]
    public AlbumDto Album { get; set; } = null!;
}
