using Microsoft.Extensions.Logging;
using Musync.Domain.Interfaces;
using Musync.Infrastructure.Auth;
using Musync.Infrastructure.Persistence;

namespace Musync.Tests.Fakes;

public sealed class FakeTokenHandler(
    AppDbContext db,
    ILogger logger,
    IAuthenticator authenticator) : TokenHandlerBase(db, logger, authenticator)
{
    protected override string TokenUrl => "https://example.com/token";
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
