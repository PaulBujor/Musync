using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using Musync.Domain;
using Musync.Domain.Interfaces;
using Musync.Infrastructure.Tidal.Models;

namespace Musync.Infrastructure.Tidal;

public sealed class TidalMusicProvider(HttpClient http, string locale) : IMusicProvider
{
    public IAsyncEnumerable<Album> GetSavedAlbumsAsync(CancellationToken ct)
    {
        throw new NotSupportedException("Tidal does not support saved album enumeration.");
    }

    public IAsyncEnumerable<Track> GetAlbumTracksAsync(string albumId, string albumName, CancellationToken ct)
    {
        throw new NotSupportedException("Tidal does not support album track enumeration via this provider.");
    }

    public IAsyncEnumerable<Track> GetPlaylistTracksAsync(string playlistId, CancellationToken ct)
    {
        throw new NotSupportedException("Tidal playlist operations are not yet supported.");
    }

    public async IAsyncEnumerable<Track> GetSavedTracksAsync([EnumeratorCancellation] CancellationToken ct)
    {
        // Nested include pulls the collection's tracks together with their artists and albums, so a
        // single request per page carries everything needed to build a Track — no follow-up lookups.
        // Relative to the client's BaseAddress, which ends in ".../v2/" — no leading slash, or the
        // "/v2" path segment would be dropped.
        var url = $"userCollectionTracks/me?locale={Uri.EscapeDataString(locale)}"
                  + "&include=items.artists,items.albums";

        while (url is not null)
        {
            var response = await http.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var page = await response.Content
                .ReadFromJsonAsync(TidalApiJsonContext.Default.TidalCollectionResponse, ct);

            var included = page?.Included;
            if (included is null || included.Count == 0)
                yield break;

            // `included` is heterogeneous — split it by resource type into name lookups.
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

            foreach (var resource in included)
            {
                if (resource.Type != "tracks" || resource.Attributes is not { } attrs)
                    continue;

                yield return new Track(
                    resource.Id ?? "",
                    attrs.Title ?? "",
                    ResolveName(resource.Relationships?.Artists, artistNames),
                    ResolveName(resource.Relationships?.Albums, albumTitles),
                    attrs.Isrc);
            }

            url = page!.Links?.Next;
        }
    }

    public Task AddTracksToPlaylistAsync(string playlistId, IEnumerable<string> trackUris, CancellationToken ct)
    {
        throw new NotSupportedException("Tidal playlist modification is not yet supported.");
    }

    public Task RemoveTracksFromPlaylistAsync(string playlistId, IEnumerable<string> trackUris, CancellationToken ct)
    {
        throw new NotSupportedException("Tidal playlist modification is not yet supported.");
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
