using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpotifyTools.Domain.Interfaces;
using SpotifyTools.Options;

namespace SpotifyTools.Infrastructure.Spotify;

public sealed class SpotifyTokenHandler : DelegatingHandler
{
    private const string TokenUrl = "https://accounts.spotify.com/api/token";
    private const string RefreshTokenKey = "spotify:refresh_token";
    private readonly ISpotifyAuthenticator _authenticator;
    private readonly ILogger<SpotifyTokenHandler> _logger;

    private readonly SpotifyOptions _options;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly IAppSettingsRepository _settings;

    private string? _accessToken;
    private DateTime _tokenExpiry = DateTime.MinValue;

    public SpotifyTokenHandler(
        IOptions<SpotifyOptions> options,
        ISpotifyAuthenticator authenticator,
        IAppSettingsRepository settings,
        ILogger<SpotifyTokenHandler> logger)
    {
        _options = options.Value;
        _authenticator = authenticator;
        _settings = settings;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        await EnsureTokenAsync(ct);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

        var response = await base.SendAsync(request, ct);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            _logger.LogWarning("Received 401. Forcing token refresh...");
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

            var refreshToken = await _settings.GetAsync(RefreshTokenKey, ct);
            if (string.IsNullOrEmpty(refreshToken))
            {
                await _authenticator.EnsureAuthenticatedAsync(ct);
                refreshToken = await _settings.GetAsync(RefreshTokenKey, ct);
            }

            await RefreshAccessTokenAsync(refreshToken!, ct);
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
            _logger.LogInformation("Spotify rotated refresh token. Persisting immediately...");
            await _settings.SetAsync(RefreshTokenKey, newToken, ct);
        }
    }
}