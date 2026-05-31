using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using SpotifyTools.Domain;
using SpotifyTools.Domain.Interfaces;
using SpotifyTools.Infrastructure.Spotify.Models;

namespace SpotifyTools.Infrastructure.Spotify;

public sealed class SpotifyMusicProvider(HttpClient http) : IMusicProvider
{
    public async IAsyncEnumerable<Album> GetSavedAlbumsAsync([EnumeratorCancellation] CancellationToken ct)
    {
        var url = "me/albums?limit=50";
        while (url is not null)
        {
            var response = await http.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var page = await response.Content
                .ReadFromJsonAsync(SpotifyApiJsonContext.Default.PagedResponseSavedAlbumItem, ct);

            if (page is null)
                yield break;

            foreach (var item in page.Items)
                yield return new Album(item.Album.Id, item.Album.Name, item.Album.Artists[0].Name);

            url = page.Next;
        }
    }

    public async IAsyncEnumerable<Track> GetAlbumTracksAsync(string albumId, string albumName,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var url = $"albums/{albumId}/tracks?limit=50";
        while (url is not null)
        {
            var response = await http.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var page = await response.Content
                .ReadFromJsonAsync(SpotifyApiJsonContext.Default.PagedResponseAlbumTrackItem, ct);

            if (page is null)
                yield break;

            foreach (var item in page.Items)
                yield return new Track(item.Id, item.Name, item.Artists[0].Name, albumName);

            url = page.Next;
        }
    }

    public async IAsyncEnumerable<Track> GetPlaylistTracksAsync(string playlistId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var url = $"playlists/{playlistId}/items?limit=50";
        while (url is not null)
        {
            var response = await http.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var page = await response.Content
                .ReadFromJsonAsync(SpotifyApiJsonContext.Default.PagedResponsePlaylistTrackItem, ct);

            if (page is null)
                yield break;

            foreach (var item in page.Items)
            {
                if (item.Item is null)
                    continue;

                yield return new Track(
                    item.Item.Id,
                    item.Item.Name,
                    item.Item.Artists[0].Name,
                    item.Item.Album.Name);
            }

            url = page.Next;
        }
    }

    public async Task<HashSet<string>> GetLikedTrackIdsAsync(CancellationToken ct)
    {
        var ids = new HashSet<string>();
        var url = "me/tracks?limit=50";
        while (url is not null)
        {
            var response = await http.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var page = await response.Content
                .ReadFromJsonAsync(SpotifyApiJsonContext.Default.PagedResponseLikedTrackItem, ct);

            if (page is null)
                break;

            foreach (var item in page.Items)
                ids.Add(item.Track.Id);

            url = page.Next;
        }

        return ids;
    }

    public async Task AddTracksToPlaylistAsync(string playlistId, IEnumerable<string> trackUris, CancellationToken ct)
    {
        var batch = new List<string>();
        foreach (var uri in trackUris)
        {
            batch.Add(uri);
            if (batch.Count < 100) continue;

            await AddBatchAsync(playlistId, batch, ct);
            batch.Clear();
        }

        if (batch.Count > 0)
            await AddBatchAsync(playlistId, batch, ct);
    }

    public async Task RemoveTracksFromPlaylistAsync(string playlistId, IEnumerable<string> trackUris,
        CancellationToken ct)
    {
        var batch = new List<string>();
        foreach (var uri in trackUris)
        {
            batch.Add(uri);
            if (batch.Count < 100) continue;

            await RemoveBatchAsync(playlistId, batch, ct);
            batch.Clear();
        }

        if (batch.Count > 0)
            await RemoveBatchAsync(playlistId, batch, ct);
    }

    private async Task AddBatchAsync(string playlistId, List<string> uris, CancellationToken ct)
    {
        var payload = new { uris = uris.Select(u => $"spotify:track:{u}").ToArray() };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await http.PostAsync($"playlists/{playlistId}/items", content, ct);
        response.EnsureSuccessStatusCode();
    }

    private async Task RemoveBatchAsync(string playlistId, List<string> uris, CancellationToken ct)
    {
        var items = uris.Select(u => new { uri = $"spotify:track:{u}" }).ToArray();
        var payload = new { items };
        var request =
            new HttpRequestMessage(HttpMethod.Delete, $"playlists/{playlistId}/items")
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
        var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }
}
