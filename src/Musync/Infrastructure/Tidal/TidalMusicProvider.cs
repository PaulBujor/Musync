using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Musync.Domain;
using Musync.Domain.Interfaces;
using Musync.Infrastructure.Tidal.Models;

namespace Musync.Infrastructure.Tidal;

public sealed class TidalMusicProvider(HttpClient http) : IMusicProvider
{
    // Max ids per Tidal filter[id] batch when resolving artist/album names.
    private const int BatchResolveChunkSize = 20;

    public IAsyncEnumerable<Album> GetSavedAlbumsAsync(CancellationToken ct)
        => throw new NotSupportedException("Tidal does not support saved album enumeration.");

    public IAsyncEnumerable<Track> GetAlbumTracksAsync(string albumId, string albumName, CancellationToken ct)
        => throw new NotSupportedException("Tidal does not support album track enumeration via this provider.");

    public IAsyncEnumerable<Track> GetPlaylistTracksAsync(string playlistId, CancellationToken ct)
        => throw new NotSupportedException("Tidal playlist operations are not yet supported.");

    public async IAsyncEnumerable<Track> GetSavedTracksAsync([EnumeratorCancellation] CancellationToken ct)
    {
        var url = "/userCollectionTracks/me/relationships/items?include=items";

        while (url is not null)
        {
            var response = await http.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var page = await response.Content
                .ReadFromJsonAsync(TidalApiJsonContext.Default.TidalCollectionPage, ct);

            if (page?.Included is null || page.Included.Count == 0)
                yield break;

            var artistIds = new HashSet<string>();
            var albumIds = new HashSet<string>();

            foreach (var track in page.Included)
            {
                if (track.Relationships?.Artists?.Data is { } artistRefs)
                    foreach (var r in artistRefs)
                        if (r.Id is not null) artistIds.Add(r.Id);

                if (track.Relationships?.Albums?.Data is { } albumRefs)
                    foreach (var r in albumRefs)
                        if (r.Id is not null) albumIds.Add(r.Id);
            }

            var artistNames = await BatchResolveArtistsAsync(artistIds, ct);
            var albumTitles = await BatchResolveAlbumsAsync(albumIds, ct);

            foreach (var track in page.Included)
            {
                var attrs = track.Attributes;
                if (attrs is null) continue;

                var artistName = track.Relationships?.Artists?.Data is { Count: > 0 }
                    && artistNames.TryGetValue(track.Relationships.Artists.Data[0].Id!, out var name)
                    ? name
                    : "";

                var albumTitle = track.Relationships?.Albums?.Data is { Count: > 0 }
                    && albumTitles.TryGetValue(track.Relationships.Albums.Data[0].Id!, out var title)
                    ? title
                    : "";

                yield return new Track(
                    track.Id ?? "",
                    attrs.Title ?? "",
                    artistName,
                    albumTitle,
                    attrs.Isrc);
            }

            url = page.Links?.Next;
        }
    }

    public Task AddTracksToPlaylistAsync(string playlistId, IEnumerable<string> trackUris, CancellationToken ct)
        => throw new NotSupportedException("Tidal playlist modification is not yet supported.");

    public Task RemoveTracksFromPlaylistAsync(string playlistId, IEnumerable<string> trackUris, CancellationToken ct)
        => throw new NotSupportedException("Tidal playlist modification is not yet supported.");

    private async Task<Dictionary<string, string>> BatchResolveArtistsAsync(
        HashSet<string> ids, CancellationToken ct)
    {
        var result = new Dictionary<string, string>();
        var idList = ids.ToList();

        for (var i = 0; i < idList.Count; i += BatchResolveChunkSize)
        {
            var batch = idList.Skip(i).Take(BatchResolveChunkSize);
            var filter = string.Join(",", batch);
            var url = $"/artists?filter[id]={filter}";

            var response = await http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) continue;

            var page = await response.Content
                .ReadFromJsonAsync(TidalApiJsonContext.Default.TidalArtistsPage, ct);

            if (page?.Included is null) continue;

            foreach (var artist in page.Included)
            {
                if (artist.Id is not null && artist.Attributes?.Name is not null)
                    result[artist.Id] = artist.Attributes.Name;
            }
        }

        return result;
    }

    private async Task<Dictionary<string, string>> BatchResolveAlbumsAsync(
        HashSet<string> ids, CancellationToken ct)
    {
        var result = new Dictionary<string, string>();
        var idList = ids.ToList();

        for (var i = 0; i < idList.Count; i += BatchResolveChunkSize)
        {
            var batch = idList.Skip(i).Take(BatchResolveChunkSize);
            var filter = string.Join(",", batch);
            var url = $"/albums?filter[id]={filter}";

            var response = await http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) continue;

            var page = await response.Content
                .ReadFromJsonAsync(TidalApiJsonContext.Default.TidalAlbumsPage, ct);

            if (page?.Included is null) continue;

            foreach (var album in page.Included)
            {
                if (album.Id is not null && album.Attributes?.Title is not null)
                    result[album.Id] = album.Attributes.Title;
            }
        }

        return result;
    }
}
