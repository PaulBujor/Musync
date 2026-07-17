using System.Text.Json.Serialization;

namespace Musync.Infrastructure.Tidal.Models;

// ── Top-level JSON:API response ────────────────────────────────

public class TidalCollectionPage
{
    [JsonPropertyName("data")]
    public List<TidalResourceIdentifier>? Data { get; set; }

    [JsonPropertyName("included")]
    public List<TidalTrackResource>? Included { get; set; }

    [JsonPropertyName("links")]
    public TidalLinks? Links { get; set; }
}

// ── Resource identifiers (data[] entries) ─────────────────────

public class TidalResourceIdentifier
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("meta")]
    public TidalResourceMeta? Meta { get; set; }
}

public class TidalResourceMeta
{
    [JsonPropertyName("addedAt")]
    public string? AddedAt { get; set; }
}

// ── Track resource (included[] entries) ────────────────────────

public class TidalTrackResource
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("attributes")]
    public TidalTrackAttributes? Attributes { get; set; }

    [JsonPropertyName("relationships")]
    public TidalTrackRelationships? Relationships { get; set; }
}

public class TidalTrackAttributes
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("isrc")]
    public string? Isrc { get; set; }
}

public class TidalTrackRelationships
{
    [JsonPropertyName("artists")]
    public TidalRelationshipData? Artists { get; set; }

    [JsonPropertyName("albums")]
    public TidalRelationshipData? Albums { get; set; }
}

public class TidalRelationshipData
{
    [JsonPropertyName("data")]
    public List<TidalResourceIdentifier>? Data { get; set; }
}

// ── Pagination links ──────────────────────────────────────────

public class TidalLinks
{
    [JsonPropertyName("self")]
    public string? Self { get; set; }

    [JsonPropertyName("next")]
    public string? Next { get; set; }

    [JsonPropertyName("meta")]
    public TidalLinksMeta? Meta { get; set; }
}

public class TidalLinksMeta
{
    [JsonPropertyName("nextCursor")]
    public string? NextCursor { get; set; }
}

// ── Artist/album resolution responses ──────────────────────────

public class TidalArtistsPage
{
    [JsonPropertyName("included")]
    public List<TidalArtistResource>? Included { get; set; }
}

public class TidalArtistResource
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("attributes")]
    public TidalArtistAttributes? Attributes { get; set; }
}

public class TidalArtistAttributes
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public class TidalAlbumsPage
{
    [JsonPropertyName("included")]
    public List<TidalAlbumResource>? Included { get; set; }
}

public class TidalAlbumResource
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("attributes")]
    public TidalAlbumAttributes? Attributes { get; set; }
}

public class TidalAlbumAttributes
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }
}
