using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SpotifyTools.Domain;
using SpotifyTools.Domain.Interfaces;
using SpotifyTools.Infrastructure.Persistence;

namespace SpotifyTools.Infrastructure.Auth;

public abstract class TokenHandlerBase(
    SpotifyDbContext db,
    ILogger logger,
    IAuthenticator authenticator) : DelegatingHandler
{
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private string? _accessToken;
    private DateTime _tokenExpiry = DateTime.MinValue;

    protected abstract string TokenUrl { get; }
    protected abstract string ProviderName { get; }

    protected abstract HttpRequestMessage CreateRefreshRequest(string refreshToken);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        await EnsureTokenAsync(ct);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

        var response = await base.SendAsync(request, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            logger.LogWarning("Received 401. Forcing {Provider} token refresh...", ProviderName);
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
                .Where(x => x.Provider == ProviderName)
                .OrderByDescending(x => x.UpdatedAt)
                .FirstOrDefaultAsync(ct);

            if (existingToken is null)
            {
                await authenticator.EnsureAuthenticatedAsync(ct);
                existingToken = await db.RefreshTokens
                    .Where(x => x.Provider == ProviderName)
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
        var request = CreateRefreshRequest(refreshToken);

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
            logger.LogInformation("{Provider} rotated refresh token. Persisting immediately...", ProviderName);

            var existing = await db.RefreshTokens
                .Where(x => x.Provider == ProviderName)
                .OrderByDescending(x => x.UpdatedAt)
                .FirstOrDefaultAsync(ct);
            if (existing is null)
            {
                db.RefreshTokens.Add(new RefreshToken
                {
                    Id = Guid.CreateVersion7(),
                    Token = newToken,
                    Provider = ProviderName,
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
