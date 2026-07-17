using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Musync.Domain.Interfaces;
using Musync.Infrastructure.Auth;
using Musync.Options;

namespace Musync.Infrastructure.Spotify;

public sealed class SpotifyTokenHandler(
    IOptions<SpotifyOptions> options,
    ISpotifyAuthenticator authenticator,
    IServiceScopeFactory scopeFactory,
    ILogger<SpotifyTokenHandler> logger)
    : TokenHandlerBase(scopeFactory, logger, authenticator)
{
    private const string TokenUrlConst = "https://accounts.spotify.com/api/token";
    private readonly SpotifyOptions _options = options.Value;

    protected override string ProviderName => ProviderKeys.Spotify;

    // Musync authenticates as a PKCE public client, so refresh sends client_id in the body
    // rather than an HTTP Basic secret. See Spotify's "Refreshing tokens" guide.
    protected override HttpRequestMessage CreateRefreshRequest(string refreshToken)
    {
        return new HttpRequestMessage(HttpMethod.Post, TokenUrlConst)
        {
            Content = new FormUrlEncodedContent([
                new KeyValuePair<string, string>("grant_type", "refresh_token"),
                new KeyValuePair<string, string>("refresh_token", refreshToken),
                new KeyValuePair<string, string>("client_id", _options.ClientId)
            ])
        };
    }
}
