using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Musync.Domain;
using Musync.Domain.Interfaces;
using Musync.Infrastructure.Tidal.Models;

namespace Musync.Infrastructure.Tidal;

public sealed class TidalMusicProvider(HttpClient http, HttpClient writeHttp, string locale) : IMusicProvider
{
    // TIDAL's OpenAPI spec caps the add/remove items `data` array at maxItems: 50. Batching at the max
    // minimises the number of (rate-limited) write requests.
    private const int PlaylistWriteBatchSize = 50;

    public async IAsyncEnumerable<Album> GetSavedAlbumsAsync([EnumeratorCancellation] CancellationToken ct)
    {
        var url = $"userCollectionAlbums/me/relationships/items?locale={Uri.EscapeDataString(locale)}"
                  + "&include=items.artists";

        await foreach (var page in GetPagesAsync(url, ct))
        {
            var included = page.Included ?? [];
            var (artistNames, _) = BuildLookups(included);

            foreach (var resource in included)
            {
                if (resource.Type != "albums" || resource.Attributes is not { } attrs)
                    continue;

                yield return new Album(
                    resource.Id ?? "",
                    attrs.Title ?? "",
                    ResolveName(resource.Relationships?.Artists, artistNames));
            }
        }
    }

    public async IAsyncEnumerable<Track> GetAlbumTracksAsync(string albumId, string albumName,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // Catalog reads accept `locale`; the album name is known from the caller, so it's threaded
        // through rather than re-resolved from an included albums resource.
        var url = $"albums/{Uri.EscapeDataString(albumId)}/relationships/items?locale={Uri.EscapeDataString(locale)}"
                  + "&include=items.artists";

        await foreach (var page in GetPagesAsync(url, ct))
            foreach (var track in ProjectTracks(page, albumName))
                yield return track;
    }

    public async IAsyncEnumerable<Track> GetPlaylistTracksAsync(string playlistId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var url = $"playlists/{Uri.EscapeDataString(playlistId)}/relationships/items?locale={Uri.EscapeDataString(locale)}"
                  + "&include=items.artists";

        // Read membership from the per-occurrence `data` linkage (not `included`, which dedupes),
        // so duplicate occurrences are visible to dedup/liked-removal/reconcile.
        await foreach (var page in GetPagesAsync(url, ct))
            foreach (var track in ProjectTracksFromData(page))
                yield return track;
    }

    public async IAsyncEnumerable<Track> GetSavedTracksAsync([EnumeratorCancellation] CancellationToken ct)
    {
        // Use the …/relationships/items sub-resource, not the singular …/me collection: only the former
        // paginates (top-level links.next). The nested include returns each track with its artists and
        // albums in one response, so there are no per-track follow-up requests.
        var url = $"userCollectionTracks/me/relationships/items?locale={Uri.EscapeDataString(locale)}"
                  + "&include=items.artists,items.albums";

        await foreach (var page in GetPagesAsync(url, ct))
            foreach (var track in ProjectTracks(page, albumName: null))
                yield return track;
    }

    public Task AddTracksToPlaylistAsync(string playlistId, IEnumerable<string> trackUris, CancellationToken ct)
    {
        var items = trackUris.Select(id => new TidalResourceIdentifier { Id = id, Type = "tracks" });
        return SendItemBatchesAsync(HttpMethod.Post, playlistId, items, ct);
    }

    // Removal is occurrence-aware: a TIDAL playlist can hold the same track id more than once, and each
    // occurrence has its own item id. Deleting a copy targets that item id (via meta.itemId), so first
    // read the playlist's linkage to find every occurrence of the requested track ids, then DELETE
    // them. Because this removes *all* occurrences of each requested track id, it matches Spotify's
    // uri-delete semantics — so the provider-agnostic reconcile and liked-removal callers work
    // unchanged, and reconcile's remove-all-then-re-add-one still yields exactly one copy.
    public async Task RemoveTracksFromPlaylistAsync(string playlistId, IEnumerable<string> trackUris,
        CancellationToken ct)
    {
        var targetTrackIds = trackUris.ToHashSet();
        if (targetTrackIds.Count == 0)
            return;

        var occurrences = new List<TidalResourceIdentifier>();
        await foreach (var link in GetPlaylistLinkageAsync(playlistId, ct))
        {
            if (link.Id is not { } id || link.Type != "tracks" || !targetTrackIds.Contains(id))
                continue;

            occurrences.Add(new TidalResourceIdentifier
            {
                Id = id,
                Type = "tracks",
                // Fall back to a plain track-id identifier if the read didn't surface an item id.
                Meta = link.Meta?.ItemId is { } itemId
                    ? new TidalResourceIdentifierMeta { ItemId = itemId }
                    : null
            });
        }

        if (occurrences.Count > 0)
            await SendItemBatchesAsync(HttpMethod.Delete, playlistId, occurrences, ct);
    }

    // Streams the playlist's per-occurrence linkage identifiers (id + meta.itemId). No `include` — the
    // item id lives on the linkage itself, so there's no need to sideload track resources here.
    private async IAsyncEnumerable<TidalResourceIdentifier> GetPlaylistLinkageAsync(string playlistId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var url = $"playlists/{Uri.EscapeDataString(playlistId)}/relationships/items"
                  + $"?locale={Uri.EscapeDataString(locale)}";

        await foreach (var page in GetPagesAsync(url, ct))
            foreach (var link in page.Data ?? [])
                yield return link;
    }

    // Pages through a JSON:API relationship collection, following links.next until it's null. TIDAL
    // returns links.next as a leading-slash path missing "/v2" — ReRoot re-roots it. Termination is
    // driven solely by links.next: a page can legitimately carry non-empty `data` with empty
    // `included` (e.g. a linkage whose track resource is region-unavailable, so not sideloaded), and
    // stopping on the first empty `included` would truncate the read and drop later items.
    private async IAsyncEnumerable<TidalCollectionResponse> GetPagesAsync(string? url,
        [EnumeratorCancellation] CancellationToken ct)
    {
        while (url is not null)
        {
            var response = await http.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var page = await response.Content
                .ReadFromJsonAsync(TidalApiJsonContext.Default.TidalCollectionResponse, ct);

            if (page is null)
                yield break;

            yield return page;

            url = ReRoot(page.Links?.Next);
        }
    }

    // Projects the `tracks` resources out of a page. When albumName is null the album title is
    // resolved from an included `albums` resource (present only when the request nested items.albums).
    private static IEnumerable<Track> ProjectTracks(TidalCollectionResponse page, string? albumName)
    {
        var included = page.Included ?? [];
        var (artistNames, albumTitles) = BuildLookups(included);

        foreach (var resource in included)
        {
            if (resource.Type != "tracks" || resource.Attributes is not { } attrs)
                continue;

            yield return new Track(
                resource.Id ?? "",
                attrs.Title ?? "",
                ResolveName(resource.Relationships?.Artists, artistNames),
                albumName ?? ResolveName(resource.Relationships?.Albums, albumTitles),
                attrs.Isrc);
        }
    }

    // Projects tracks from the primary `data` linkage (one Track per occurrence, in order), resolving
    // display fields from the deduplicated `included` sideload. Unlike ProjectTracks (which reads
    // `included` directly and so collapses duplicates), this preserves repeated occurrences — required
    // for playlists, where the same track id can appear multiple times. A linkage whose track resource
    // isn't sideloaded still yields the id (with blank display fields) so membership/dup counts stay
    // complete.
    private static IEnumerable<Track> ProjectTracksFromData(TidalCollectionResponse page)
    {
        var included = page.Included ?? [];
        var (artistNames, albumTitles) = BuildLookups(included);
        var trackResources = included
            .Where(r => r.Type == "tracks" && r.Id is not null)
            .GroupBy(r => r.Id!)
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var link in page.Data ?? [])
        {
            if (link.Type != "tracks" || link.Id is not { } id)
                continue;

            var addedAt = link.Meta?.AddedAt;

            if (trackResources.TryGetValue(id, out var res) && res.Attributes is { } attrs)
                yield return new Track(
                    id,
                    attrs.Title ?? "",
                    ResolveName(res.Relationships?.Artists, artistNames),
                    ResolveName(res.Relationships?.Albums, albumTitles),
                    attrs.Isrc,
                    addedAt);
            else
                yield return new Track(id, "", "", "", null, addedAt);
        }
    }

    // `included` is heterogeneous — split it by resource type into id→name lookups.
    private static (Dictionary<string, string> ArtistNames, Dictionary<string, string> AlbumTitles) BuildLookups(
        List<TidalResource> included)
    {
        var artistNames = new Dictionary<string, string>();
        var albumTitles = new Dictionary<string, string>();

        foreach (var resource in included)
        {
            if (resource.Id is not { } id)
                continue;

            if (resource.Type == "artists" && resource.Attributes?.Name is { } name)
                artistNames[id] = name;
            else if (resource.Type == "albums" && resource.Attributes?.Title is { } albumTitle)
                albumTitles[id] = albumTitle;
        }

        return (artistNames, albumTitles);
    }

    private async Task SendItemBatchesAsync(HttpMethod method, string playlistId,
        IEnumerable<TidalResourceIdentifier> items, CancellationToken ct)
    {
        foreach (var batch in items.Chunk(PlaylistWriteBatchSize))
        {
            var payload = new TidalRelationshipData { Data = batch.ToList() };
            var json = JsonSerializer.Serialize(payload, TidalApiJsonContext.Default.TidalRelationshipData);

            using var request = new HttpRequestMessage(method,
                $"playlists/{Uri.EscapeDataString(playlistId)}/relationships/items")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/vnd.api+json")
            };

            var response = await writeHttp.SendAsync(request, ct);

            // A token without 'playlists.write' — or a playlist the user doesn't own — returns 403.
            // Surface that plainly rather than a bare "403 Forbidden", since the fix is usually
            // re-authenticating with the right scope or pointing at a playlist you own.
            if (response.StatusCode == HttpStatusCode.Forbidden)
                throw new InvalidOperationException(
                    "Tidal playlist write forbidden (403). Ensure the access token was granted the "
                    + "'playlists.write' scope (re-authenticate after updating Tidal:Scopes) and that "
                    + "Tidal:QueuePlaylistId is a playlist your account owns.");

            response.EnsureSuccessStatusCode();
        }
    }

    // TIDAL returns links.next as a leading-slash path missing the "/v2" version segment (e.g.
    // "/userCollectionTracks/…"). Handed verbatim to HttpClient with a ".../v2/" base, the leading
    // slash resolves against the host root and drops /v2 → 404. Stripping the leading slash re-roots
    // it under the base's "/v2/". Absolute URLs (and null) are passed through unchanged.
    private static string? ReRoot(string? next)
    {
        if (string.IsNullOrEmpty(next))
            return null;

        return Uri.IsWellFormedUriString(next, UriKind.Absolute) ? next : next.TrimStart('/');
    }

    // First related resource's resolved name, or "" when the relationship or lookup is missing.
    private static string ResolveName(TidalRelationshipData? relationship, Dictionary<string, string> names)
    {
        if (relationship?.Data is { Count: > 0 } data
            && data[0].Id is { } id
            && names.TryGetValue(id, out var name))
            return name;

        return "";
    }
}
