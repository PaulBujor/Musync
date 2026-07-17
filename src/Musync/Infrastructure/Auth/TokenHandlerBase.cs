using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Musync.Domain;
using Musync.Domain.Interfaces;
using Musync.Infrastructure.Persistence;

namespace Musync.Infrastructure.Auth;

public abstract class TokenHandlerBase(
    IServiceScopeFactory scopeFactory,
    ILogger logger,
    IAuthenticator authenticator) : DelegatingHandler
{
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private string? _accessToken;
    private DateTime _tokenExpiry = DateTime.MinValue;

    protected abstract string ProviderName { get; }

    protected abstract HttpRequestMessage CreateRefreshRequest(string refreshToken);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        await EnsureTokenAsync(ct);

        // The 401 path resends the request, but sending consumes its content stream, so keep a
        // buffered clone to retry with (a POST/DELETE body would otherwise be empty on retry).
        var retryRequest = await CloneRequestAsync(request, ct);

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        var response = await base.SendAsync(request, ct);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            logger.LogWarning("Received 401. Forcing {Provider} token refresh...", ProviderName);
            _accessToken = null;
            await EnsureTokenAsync(ct);
            retryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            response = await base.SendAsync(retryRequest, ct);
        }

        return response;
    }

    private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri) { Version = request.Version };

        foreach (var header in request.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

        if (request.Content is not null)
        {
            var bytes = await request.Content.ReadAsByteArrayAsync(ct);
            clone.Content = new ByteArrayContent(bytes);
            foreach (var header in request.Content.Headers)
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return clone;
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

            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

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

            var refreshed = await TryRefreshAccessTokenAsync(db, existingToken!.Token, ct);
            if (!refreshed)
            {
                logger.LogWarning("{Provider} refresh token expired. Re-authenticating...", ProviderName);

                await authenticator.EnsureAuthenticatedAsync(ct);

                var newToken = await db.RefreshTokens
                    .Where(x => x.Provider == ProviderName)
                    .OrderByDescending(x => x.UpdatedAt)
                    .FirstOrDefaultAsync(ct);

                if (newToken is null)
                    throw new InvalidOperationException(
                        $"Failed to obtain a new {ProviderName} refresh token after expiry.");

                await TryRefreshAccessTokenAsync(db, newToken.Token, ct);
            }
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task<bool> TryRefreshAccessTokenAsync(AppDbContext db, string refreshToken, CancellationToken ct)
    {
        var request = CreateRefreshRequest(refreshToken);

        var response = await base.SendAsync(request, ct);

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("error", out var errorProp))
                {
                    var error = errorProp.GetString();
                    if (error == "invalid_grant")
                    {
                        logger.LogWarning(
                            "{Provider} refresh token rejected (invalid_grant). Deleting expired token...",
                            ProviderName);

                        var expired = await db.RefreshTokens
                            .Where(x => x.Provider == ProviderName)
                            .OrderByDescending(x => x.UpdatedAt)
                            .FirstOrDefaultAsync(ct);
                        if (expired is not null)
                        {
                            db.RefreshTokens.Remove(expired);
                            await db.SaveChangesAsync(ct);
                        }

                        return false;
                    }
                }
            }
            catch (JsonException)
            {
                logger.LogWarning(
                    "{Provider} refresh error response was not valid JSON: {Body}",
                    ProviderName, body);
            }
        }

        response.EnsureSuccessStatusCode();

        using var refreshDoc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

        var root = refreshDoc.RootElement;
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

        return true;
    }
}
