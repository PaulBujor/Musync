using System.Text.Json.Serialization;

namespace SpotifyTools.Infrastructure.Spotify.Models;

public class PlaylistTrackItem
{
    [JsonPropertyName("track")]
    public PlaylistTrackDto Track { get; set; } = null!;
}

public class PlaylistTrackDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("artists")]
    public List<ArtistDto> Artists { get; set; } = [];

    [JsonPropertyName("album")]
    public AlbumRefDto Album { get; set; } = null!;
}

public class AlbumRefDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}
