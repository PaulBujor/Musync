using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using SpotifyTools.Domain;
using SpotifyTools.Domain.Interfaces;

namespace SpotifyTools.Infrastructure.Spotify;

public sealed class SpotifyMusicProvider(HttpClient http) : IMusicProvider
{
    public async IAsyncEnumerable<Album> GetSavedAlbumsAsync([EnumeratorCancellation] CancellationToken ct)
    {
        var url = "https://api.spotify.com/v1/me/albums?limit=50";
        while (url is not null)
        {
            var response = await http.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            using var doc =
                await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var root = doc.RootElement;

            foreach (var item in root.GetProperty("items").EnumerateArray())
            {
                var album = item.GetProperty("album");
                yield return new Album(
                    album.GetProperty("id").GetString()!,
                    album.GetProperty("name").GetString()!,
                    album.GetProperty("artists")[0].GetProperty("name").GetString()!
                );
            }

            url = root.TryGetProperty("next", out var next) && next.ValueKind == JsonValueKind.String
                ? next.GetString()
                : null;
        }
    }

    public async IAsyncEnumerable<Track> GetAlbumTracksAsync(string albumId, string albumName,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var url = $"https://api.spotify.com/v1/albums/{albumId}/tracks?limit=50";
        while (url is not null)
        {
            var response = await http.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            using var doc =
                await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var root = doc.RootElement;

            foreach (var item in root.GetProperty("items").EnumerateArray())
                yield return new Track(
                    item.GetProperty("id").GetString()!,
                    item.GetProperty("name").GetString()!,
                    item.GetProperty("artists")[0].GetProperty("name").GetString()!,
                    albumName
                );

            url = root.TryGetProperty("next", out var next) && next.ValueKind == JsonValueKind.String
                ? next.GetString()
                : null;
        }
    }

    public async IAsyncEnumerable<Track> GetPlaylistTracksAsync(string playlistId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var url = $"https://api.spotify.com/v1/playlists/{playlistId}/tracks?limit=50";
        while (url is not null)
        {
            var response = await http.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            using var doc =
                await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var root = doc.RootElement;

            foreach (var item in root.GetProperty("items").EnumerateArray())
            {
                var track = item.GetProperty("track");
                if (track.ValueKind == JsonValueKind.Null)
                    continue;

                yield return new Track(
                    track.GetProperty("id").GetString()!,
                    track.GetProperty("name").GetString()!,
                    track.GetProperty("artists")[0].GetProperty("name").GetString()!,
                    track.GetProperty("album").GetProperty("name").GetString()!
                );
            }

            url = root.TryGetProperty("next", out var next) && next.ValueKind == JsonValueKind.String
                ? next.GetString()
                : null;
        }
    }

    public async Task<HashSet<string>> GetLikedTrackIdsAsync(CancellationToken ct)
    {
        var ids = new HashSet<string>();
        var url = "https://api.spotify.com/v1/me/tracks?limit=50";
        while (url is not null)
        {
            var response = await http.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            using var doc =
                await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var root = doc.RootElement;

            foreach (var item in root.GetProperty("items").EnumerateArray())
            {
                var track = item.GetProperty("track");
                ids.Add(track.GetProperty("id").GetString()!);
            }

            url = root.TryGetProperty("next", out var next) && next.ValueKind == JsonValueKind.String
                ? next.GetString()
                : null;
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
        var response = await http.PostAsync($"https://api.spotify.com/v1/playlists/{playlistId}/tracks", content, ct);
        response.EnsureSuccessStatusCode();
    }

    private async Task RemoveBatchAsync(string playlistId, List<string> uris, CancellationToken ct)
    {
        var tracks = uris.Select(u => new { uri = $"spotify:track:{u}" }).ToArray();
        var payload = new { tracks };
        var request =
            new HttpRequestMessage(HttpMethod.Delete, $"https://api.spotify.com/v1/playlists/{playlistId}/tracks")
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
        var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }
}