using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpotifyTools.Domain.Interfaces;
using SpotifyTools.Infrastructure.Auth;
using SpotifyTools.Infrastructure.Persistence;
using SpotifyTools.Options;

namespace SpotifyTools.Infrastructure.Spotify;

public sealed class SpotifyTokenHandler(
    IOptions<SpotifyOptions> options,
    ISpotifyAuthenticator authenticator,
    AppDbContext db,
    ILogger<SpotifyTokenHandler> logger)
    : TokenHandlerBase(db, logger, authenticator)
{
    private const string TokenUrlConst = "https://accounts.spotify.com/api/token";
    private readonly SpotifyOptions _options = options.Value;

    protected override string TokenUrl => TokenUrlConst;
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
