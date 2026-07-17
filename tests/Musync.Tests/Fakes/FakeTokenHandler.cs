using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Musync.Domain.Interfaces;
using Musync.Infrastructure.Auth;

namespace Musync.Tests.Fakes;

public sealed class FakeTokenHandler(
    IServiceScopeFactory scopeFactory,
    ILogger logger,
    IAuthenticator authenticator) : TokenHandlerBase(scopeFactory, logger, authenticator)
{
    private const string TokenUrl = "https://example.com/token";

    protected override string ProviderName => "test";

    protected override HttpRequestMessage CreateRefreshRequest(string refreshToken)
    {
        return new HttpRequestMessage(HttpMethod.Post, TokenUrl)
        {
            Content = new FormUrlEncodedContent([
                new KeyValuePair<string, string>("grant_type", "refresh_token"),
                new KeyValuePair<string, string>("refresh_token", refreshToken)
            ])
        };
    }
}