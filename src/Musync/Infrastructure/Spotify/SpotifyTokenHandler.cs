using System.Net.Http.Headers;
using System.Text;
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

    protected override string ProviderName => "spotify";

    protected override HttpRequestMessage CreateRefreshRequest(string refreshToken)
    {
        var authBytes = Encoding.ASCII.GetBytes($"{_options.ClientId}:{_options.ClientSecret}");
        var request = new HttpRequestMessage(HttpMethod.Post, TokenUrlConst)
        {
            Content = new FormUrlEncodedContent([
                new KeyValuePair<string, string>("grant_type", "refresh_token"),
                new KeyValuePair<string, string>("refresh_token", refreshToken)
            ])
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(authBytes));
        return request;
    }
}
