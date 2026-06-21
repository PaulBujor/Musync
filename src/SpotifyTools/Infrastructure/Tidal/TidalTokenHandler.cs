using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpotifyTools.Domain.Interfaces;
using SpotifyTools.Infrastructure.Auth;
using SpotifyTools.Infrastructure.Persistence;
using SpotifyTools.Options;

namespace SpotifyTools.Infrastructure.Tidal;

public sealed class TidalTokenHandler(
    IOptions<TidalOptions> options,
    ITidalAuthenticator authenticator,
    SpotifyDbContext db,
    ILogger<TidalTokenHandler> logger)
    : TokenHandlerBase(db, logger, authenticator)
{
    private const string TokenUrlConst = "https://auth.tidal.com/v1/oauth2/token";
    private readonly TidalOptions _options = options.Value;

    protected override string TokenUrl => TokenUrlConst;
    protected override string ProviderName => "tidal";

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
