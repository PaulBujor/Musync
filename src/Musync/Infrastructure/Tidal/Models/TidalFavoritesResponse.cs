using System.Text.Json.Serialization;

namespace Musync.Infrastructure.Tidal.Models;

// TIDAL v2 is a JSON:API. A collection read with `include=items.artists,items.albums` returns the
// requested tracks plus their artists and albums together in a single, heterogeneous `included`
// array, discriminated by `type` ("tracks" | "artists" | "albums").

public sealed class TidalCollectionResponse
{
    [JsonPropertyName("included")]
    public List<TidalResource>? Included { get; init; }

    [JsonPropertyName("links")]
    public TidalLinks? Links { get; init; }
}

public sealed class TidalResource
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("attributes")]
    public TidalResourceAttributes? Attributes { get; init; }

    [JsonPropertyName("relationships")]
    public TidalTrackRelationships? Relationships { get; init; }
}

public sealed class TidalResourceAttributes
{
    // Present on tracks and albums.
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    // Present on artists.
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    // Present on tracks.
    [JsonPropertyName("isrc")]
    public string? Isrc { get; init; }
}

public sealed class TidalTrackRelationships
{
    [JsonPropertyName("artists")]
    public TidalRelationshipData? Artists { get; init; }

    [JsonPropertyName("albums")]
    public TidalRelationshipData? Albums { get; init; }
}

public sealed class TidalRelationshipData
{
    [JsonPropertyName("data")]
    public List<TidalResourceIdentifier>? Data { get; init; }
}

public sealed class TidalResourceIdentifier
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }
}

public sealed class TidalLinks
{
    [JsonPropertyName("self")]
    public string? Self { get; init; }

    [JsonPropertyName("next")]
    public string? Next { get; init; }
}
