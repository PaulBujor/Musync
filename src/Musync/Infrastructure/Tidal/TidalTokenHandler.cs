using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Musync.Domain.Interfaces;
using Musync.Infrastructure.Auth;
using Musync.Options;

namespace Musync.Infrastructure.Tidal;

public sealed class TidalTokenHandler(
    IOptions<TidalOptions> options,
    ITidalAuthenticator authenticator,
    IServiceScopeFactory scopeFactory,
    ILogger<TidalTokenHandler> logger)
    : TokenHandlerBase(scopeFactory, logger, authenticator)
{
    private const string TokenUrlConst = "https://auth.tidal.com/v1/oauth2/token";
    private readonly TidalOptions _options = options.Value;

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
