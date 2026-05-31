using System.Net.Http.Json;
using System.Text.Json;
using SpotifyTools.Domain;
using SpotifyTools.Domain.Interfaces;
using SpotifyTools.Infrastructure.Spotify.Models;

namespace SpotifyTools.Infrastructure.Mapping;

public sealed class SpotifyTrackMapper(HttpClient http) : ITrackMapper
{
    public async Task<string?> FindSpotifyTrackIdAsync(Track track, CancellationToken ct)
    {
        // Try ISRC first
        if (!string.IsNullOrEmpty(track.Isrc))
        {
            var result = await SearchByQueryAsync($"isrc:{track.Isrc}", ct);
            if (result is not null)
                return result;
        }

        // Fall back to name + artist search
        var fallback = await SearchByQueryAsync(
            $"track:{track.Name} artist:{track.Artist}", ct);

        return fallback;
    }

    private async Task<string?> SearchByQueryAsync(string query, CancellationToken ct)
    {
        var url = $"search?q={Uri.EscapeDataString(query)}&type=track&limit=1";
        var response = await http.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
            return null;

        using var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

        var items = doc.RootElement
            .GetProperty("tracks")
            .GetProperty("items");

        if (items.GetArrayLength() == 0)
            return null;

        return items[0].GetProperty("id").GetString();
    }
}
