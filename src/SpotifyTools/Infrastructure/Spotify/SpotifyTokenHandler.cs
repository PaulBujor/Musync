using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpotifyTools.Domain;
using SpotifyTools.Domain.Interfaces;
using SpotifyTools.Infrastructure.Persistence;
using SpotifyTools.Options;

namespace SpotifyTools.Infrastructure.Spotify;

public sealed class SpotifyTokenHandler(
    IOptions<SpotifyOptions> options,
    ISpotifyAuthenticator authenticator,
    SpotifyDbContext db,
    ILogger<SpotifyTokenHandler> logger)
    : DelegatingHandler
{
    private const string TokenUrl = "https://accounts.spotify.com/api/token";

    private readonly SpotifyOptions _options = options.Value;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private string? _accessToken;
    private DateTime _tokenExpiry = DateTime.MinValue;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        await EnsureTokenAsync(ct);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

        var response = await base.SendAsync(request, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            logger.LogWarning("Received 401. Forcing token refresh...");
            _accessToken = null;
            await EnsureTokenAsync(ct);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            response = await base.SendAsync(request, ct);
        }

        return response;
    }

    private async Task EnsureTokenAsync(CancellationToken ct)
    {
        if (_accessToken != null && DateTime.UtcNow < _tokenExpiry)
            return;

        await _refreshLock.WaitAsync(ct);
        try
        {
            if (_accessToken != null && DateTime.UtcNow < _tokenExpiry)
                return;

            var existingToken = await db.RefreshTokens
                .Where(x => x.Provider == "spotify")
                .OrderByDescending(x => x.UpdatedAt)
                .FirstOrDefaultAsync(ct);

            if (existingToken is null)
            {
                await authenticator.EnsureAuthenticatedAsync(ct);
                existingToken = await db.RefreshTokens
                    .Where(x => x.Provider == "spotify")
                    .OrderByDescending(x => x.UpdatedAt)
                    .FirstOrDefaultAsync(ct);
            }

            await RefreshAccessTokenAsync(existingToken!.Token, ct);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task RefreshAccessTokenAsync(string refreshToken, CancellationToken ct)
    {
        var authBytes = Encoding.ASCII.GetBytes($"{_options.ClientId}:{_options.ClientSecret}");
        var request = new HttpRequestMessage(HttpMethod.Post, TokenUrl)
        {
            Content = new FormUrlEncodedContent([
                new KeyValuePair<string, string>("grant_type", "refresh_token"),
                new KeyValuePair<string, string>("refresh_token", refreshToken)
            ])
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(authBytes));

        var response = await base.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

        var root = doc.RootElement;
        _accessToken = root.GetProperty("access_token").GetString()!;
        var expiresIn = root.GetProperty("expires_in").GetInt32();
        _tokenExpiry = DateTime.UtcNow.AddSeconds(expiresIn - 60);

        if (root.TryGetProperty("refresh_token", out var newRefreshToken))
        {
            var newToken = newRefreshToken.GetString()!;
            logger.LogInformation("Spotify rotated refresh token. Persisting immediately...");

            var existing = await db.RefreshTokens
                .Where(x => x.Provider == "spotify")
                .OrderByDescending(x => x.UpdatedAt)
                .FirstOrDefaultAsync(ct);
            if (existing is null)
            {
                db.RefreshTokens.Add(new RefreshToken
                {
                    Id = Guid.CreateVersion7(),
                    Token = newToken,
                    Provider = "spotify",
                    UpdatedAt = DateTime.UtcNow
                });
            }
            else
            {
                existing.Token = newToken;
                existing.UpdatedAt = DateTime.UtcNow;
            }
            await db.SaveChangesAsync(ct);
        }
    }
}
