using System.Text.Json.Serialization;

namespace SpotifyTools.Infrastructure.Tidal.Models;

public class TidalFavoritesPage
{
    [JsonPropertyName("limit")]
    public int Limit { get; set; }

    [JsonPropertyName("offset")]
    public int Offset { get; set; }

    [JsonPropertyName("totalNumberOfItems")]
    public int TotalNumberOfItems { get; set; }

    [JsonPropertyName("items")]
    public List<TidalFavoriteItem>? Items { get; set; }
}

public class TidalFavoriteItem
{
    [JsonPropertyName("item")]
    public TidalTrackDto? Item { get; set; }
}

public class TidalTrackDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("isrc")]
    public string? Isrc { get; set; }

    [JsonPropertyName("artist")]
    public TidalArtistRef? Artist { get; set; }

    [JsonPropertyName("artists")]
    public List<TidalArtistDto>? Artists { get; set; }

    [JsonPropertyName("album")]
    public TidalAlbumRef? Album { get; set; }
}

public class TidalArtistRef
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public class TidalArtistDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }
}

public class TidalAlbumRef
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }
}
