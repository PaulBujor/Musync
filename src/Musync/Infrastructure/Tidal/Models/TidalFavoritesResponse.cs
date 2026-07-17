using System.Text.Json.Serialization;

namespace Musync.Infrastructure.Tidal.Models;

// ── Top-level JSON:API response ────────────────────────────────

public sealed class TidalCollectionPage
{
    [JsonPropertyName("data")]
    public List<TidalResourceIdentifier>? Data { get; init; }

    [JsonPropertyName("included")]
    public List<TidalTrackResource>? Included { get; init; }

    [JsonPropertyName("links")]
    public TidalLinks? Links { get; init; }
}

// ── Resource identifiers (data[] entries) ─────────────────────

public sealed class TidalResourceIdentifier
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("meta")]
    public TidalResourceMeta? Meta { get; init; }
}

public sealed class TidalResourceMeta
{
    [JsonPropertyName("addedAt")]
    public string? AddedAt { get; init; }
}

// ── Track resource (included[] entries) ────────────────────────

public sealed class TidalTrackResource
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("attributes")]
    public TidalTrackAttributes? Attributes { get; init; }

    [JsonPropertyName("relationships")]
    public TidalTrackRelationships? Relationships { get; init; }
}

public sealed class TidalTrackAttributes
{
    [JsonPropertyName("title")]
    public string? Title { get; init; }

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

// ── Pagination links ──────────────────────────────────────────

public sealed class TidalLinks
{
    [JsonPropertyName("self")]
    public string? Self { get; init; }

    [JsonPropertyName("next")]
    public string? Next { get; init; }

    [JsonPropertyName("meta")]
    public TidalLinksMeta? Meta { get; init; }
}

public sealed class TidalLinksMeta
{
    [JsonPropertyName("nextCursor")]
    public string? NextCursor { get; init; }
}

// ── Artist/album resolution responses ──────────────────────────

public sealed class TidalArtistsPage
{
    [JsonPropertyName("included")]
    public List<TidalArtistResource>? Included { get; init; }
}

public sealed class TidalArtistResource
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("attributes")]
    public TidalArtistAttributes? Attributes { get; init; }
}

public sealed class TidalArtistAttributes
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }
}

public sealed class TidalAlbumsPage
{
    [JsonPropertyName("included")]
    public List<TidalAlbumResource>? Included { get; init; }
}

public sealed class TidalAlbumResource
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("attributes")]
    public TidalAlbumAttributes? Attributes { get; init; }
}

public sealed class TidalAlbumAttributes
{
    [JsonPropertyName("title")]
    public string? Title { get; init; }
}
