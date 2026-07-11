using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Musync.Domain;
using Musync.Domain.Interfaces;
using Musync.Infrastructure.Spotify.Models;

namespace Musync.Infrastructure.Spotify;

public sealed class SpotifySearchMapper(
    HttpClient http,
    ILogger<SpotifySearchMapper> logger) : ITrackMapper
{
    public async Task<string?> FindTargetTrackIdAsync(Track track, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(track.Isrc))
        {
            var result = await SearchByQueryAsync($"isrc:{track.Isrc}", ct);
            if (result is not null)
                return result;
        }

        var fallback = await SearchByQueryAsync(
            $"track:{track.Name} artist:{track.Artist}", ct);

        return fallback;
    }

    private async Task<string?> SearchByQueryAsync(string query, CancellationToken ct)
    {
        var url = $"search?q={Uri.EscapeDataString(query)}&type=track&limit=1";
        var response = await http.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Track search failed: {StatusCode} for query '{Query}'", (int)response.StatusCode, query);
            return null;
        }

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
