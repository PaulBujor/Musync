using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using SpotifyTools.Domain;
using SpotifyTools.Domain.Interfaces;
using SpotifyTools.Infrastructure.Tidal.Models;

namespace SpotifyTools.Infrastructure.Tidal;

public sealed class TidalMusicProvider(HttpClient http) : IMusicProvider
{
    public IAsyncEnumerable<Album> GetSavedAlbumsAsync(CancellationToken ct)
        => throw new NotSupportedException("Tidal does not support saved album enumeration.");

    public IAsyncEnumerable<Track> GetAlbumTracksAsync(string albumId, string albumName, CancellationToken ct)
        => throw new NotSupportedException("Tidal does not support album track enumeration via this provider.");

    public IAsyncEnumerable<Track> GetPlaylistTracksAsync(string playlistId, CancellationToken ct)
        => throw new NotSupportedException("Tidal playlist operations are not yet supported.");

    public async IAsyncEnumerable<Track> GetSavedTracksAsync([EnumeratorCancellation] CancellationToken ct)
    {
        var userId = await GetUserIdAsync(ct);
        var offset = 0;

        while (true)
        {
            var url = $"https://api.tidal.com/v1/users/{userId}/favorites/tracks" +
                       $"?limit=100&offset={offset}&countryCode=US";

            var response = await http.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var page = await response.Content
                .ReadFromJsonAsync(TidalApiJsonContext.Default.TidalFavoritesPage, ct);

            if (page?.Items is null || page.Items.Count == 0)
                yield break;

            foreach (var item in page.Items)
            {
                var track = item.Item;
                if (track is null) continue;

                var artistName = track.Artists?.FirstOrDefault()?.Name ?? track.Artist?.Name ?? "";
                var albumName = track.Album?.Title ?? "";

                yield return new Track(
                    track.Id.ToString(),
                    track.Title ?? "",
                    artistName,
                    albumName,
                    track.Isrc);
            }

            if (offset + page.Limit >= page.TotalNumberOfItems)
                yield break;

            offset += page.Limit;
        }
    }

    public async Task<HashSet<string>> GetLikedTrackIdsAsync(CancellationToken ct)
    {
        var ids = new HashSet<string>();
        await foreach (var track in GetSavedTracksAsync(ct))
            ids.Add(track.Id);
        return ids;
    }

    public Task AddTracksToPlaylistAsync(string playlistId, IEnumerable<string> trackUris, CancellationToken ct)
        => throw new NotSupportedException("Tidal playlist modification is not yet supported.");

    public Task RemoveTracksFromPlaylistAsync(string playlistId, IEnumerable<string> trackUris, CancellationToken ct)
        => throw new NotSupportedException("Tidal playlist modification is not yet supported.");

    private async Task<string> GetUserIdAsync(CancellationToken ct)
    {
        var response = await http.GetAsync("https://openapi.tidal.com/v2/users/me", ct);
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var userId = doc.RootElement.GetProperty("data").GetProperty("id").GetString();
        return userId ?? throw new InvalidOperationException("Could not determine Tidal user ID.");
    }
}
