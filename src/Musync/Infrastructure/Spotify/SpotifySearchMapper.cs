using System.Text.Json;
using Microsoft.Extensions.Logging;
using Musync.Domain;
using Musync.Domain.Interfaces;

namespace Musync.Infrastructure.Spotify;

public sealed class SpotifySearchMapper(
    HttpClient http,
    ILogger<SpotifySearchMapper> logger) : ITrackMapper
{
    // A few candidates so the name+artist fallback can pick an ISRC match, not just the top hit.
    private const int FallbackCandidateLimit = 5;

    public async Task<string?> FindTargetTrackIdAsync(Track track, CancellationToken ct)
    {
        // ISRC is a stable cross-provider identifier, so an isrc: match is authoritative.
        if (!string.IsNullOrEmpty(track.Isrc))
        {
            var byIsrc = await SearchAsync($"isrc:{track.Isrc}", 1, ct);
            if (byIsrc.Count > 0)
                return byIsrc[0].Id;
        }

        var candidates = await SearchAsync(
            $"track:{track.Name} artist:{track.Artist}", FallbackCandidateLimit, ct);
        if (candidates.Count == 0)
            return null;

        // When the source has an ISRC, only trust a candidate whose ISRC matches — blindly taking
        // the top hit risks a wrong recording (live/remaster/cover). No match → treat as unmatched.
        if (!string.IsNullOrEmpty(track.Isrc))
            return candidates.FirstOrDefault(c => c.Isrc == track.Isrc)?.Id;

        return candidates[0].Id;
    }

    private async Task<List<TrackCandidate>> SearchAsync(string query, int limit, CancellationToken ct)
    {
        var url = $"search?q={Uri.EscapeDataString(query)}&type=track&limit={limit}";
        var response = await http.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Track search failed: {StatusCode} for query '{Query}'", (int)response.StatusCode, query);
            return [];
        }

        using var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

        var items = doc.RootElement
            .GetProperty("tracks")
            .GetProperty("items");

        var results = new List<TrackCandidate>(items.GetArrayLength());
        foreach (var item in items.EnumerateArray())
        {
            if (item.GetProperty("id").GetString() is not { } id)
                continue;

            string? isrc = null;
            if (item.TryGetProperty("external_ids", out var externalIds)
                && externalIds.TryGetProperty("isrc", out var isrcElement))
                isrc = isrcElement.GetString();

            results.Add(new TrackCandidate(id, isrc));
        }

        return results;
    }

    private sealed record TrackCandidate(string Id, string? Isrc);
}