using System.Text.Json.Serialization;

namespace Musync.Infrastructure.Tidal.Models;

// TIDAL v2 is a JSON:API. A collection read with `include=items.artists,items.albums` returns the
// requested tracks plus their artists and albums together in a single, heterogeneous `included`
// array, discriminated by `type` ("tracks" | "artists" | "albums").

public sealed class TidalCollectionResponse
{
    // The primary linkage array. For a …/relationships/items read this is the ordered, per-occurrence
    // list of items — a track present twice appears twice here, whereas `included` (below) is
    // deduplicated by (type,id) per the JSON:API spec and so cannot reveal duplicates.
    [JsonPropertyName("data")]
    public List<TidalResourceIdentifier>? Data { get; init; }

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

    // Per-occurrence metadata on a playlist item linkage. `itemId` uniquely identifies one occurrence
    // of a track in the playlist, so it's what a DELETE must target to remove a specific copy. Omitted
    // when writing (WhenWritingNull) so add/remove payloads that don't set it stay `{"id","type"}`.
    [JsonPropertyName("meta")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TidalResourceIdentifierMeta? Meta { get; init; }
}

public sealed class TidalResourceIdentifierMeta
{
    [JsonPropertyName("itemId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ItemId { get; init; }

    // When the item was added to the playlist. Read-only; used to keep the earliest-added copy when
    // de-duplicating. Omitted on write so it never bloats an add/remove payload.
    [JsonPropertyName("addedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? AddedAt { get; init; }
}

public sealed class TidalLinks
{
    [JsonPropertyName("self")]
    public string? Self { get; init; }

    [JsonPropertyName("next")]
    public string? Next { get; init; }
}
