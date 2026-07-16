using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Musync.Domain;
using Musync.Infrastructure.Persistence;
using Musync.Tests.Fakes;

namespace Musync.Tests.Jobs;

public sealed class TokenHandlerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly TestScopeFactory _scopeFactory;

    public TokenHandlerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        _db = new AppDbContext(options);
        _db.Database.OpenConnection();
        _db.Database.EnsureCreated();

        _scopeFactory = new TestScopeFactory(_db);
    }

    public void Dispose()
    {
        _db.Database.CloseConnection();
        _db.Dispose();
    }

    private sealed class TestScopeFactory(AppDbContext db) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new TestScope(db);

        private sealed class TestScope(AppDbContext db) : IServiceScope
        {
            public IServiceProvider ServiceProvider => new TestServiceProvider(db);
            public void Dispose() { }
        }

        private sealed class TestServiceProvider(AppDbContext db) : IServiceProvider
        {
            public object? GetService(Type serviceType)
            {
                if (serviceType == typeof(AppDbContext))
                    return db;
                return null;
            }
        }
    }

    [Fact]
    public async Task SendAsync_InvalidGrantResponse_DeletesExpiredTokenAndReauthenticates()
    {
        _db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.CreateVersion7(),
            Token = "expired-spotify-token",
            Provider = "test",
            UpdatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var authenticator = new MockAuthenticator(_db);
        var logger = NullLoggerFactory.Instance.CreateLogger("FakeTokenHandler");

        var tokenHandler = new FakeTokenHandler(_scopeFactory, logger, authenticator);

        var refreshAttempts = 0;
        tokenHandler.InnerHandler = new MockHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsoluteUri == "https://example.com/token")
            {
                refreshAttempts++;
                if (refreshAttempts == 1)
                {
                    return new HttpResponseMessage(HttpStatusCode.BadRequest)
                    {
                        Content = new StringContent(
                            """{"error":"invalid_grant","error_description":"Refresh token expired"}""",
                            Encoding.UTF8,
                            "application/json")
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"access_token":"new-access-token","expires_in":3600}""",
                        Encoding.UTF8,
                        "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"data":"ok"}""", Encoding.UTF8, "application/json")
            };
        });

        using var client = new HttpClient(tokenHandler);

        var response = await client.GetAsync("https://example.com/api/resource");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, authenticator.CallCount);
        Assert.DoesNotContain(_db.RefreshTokens, t => t.Token == "expired-spotify-token");
    }

    [Fact]
    public async Task SendAsync_ValidRefreshToken_AttachesBearerToken()
    {
        _db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.CreateVersion7(),
            Token = "valid-refresh-token",
            Provider = "test",
            UpdatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var authenticator = new MockAuthenticator(_db);
        var logger = NullLoggerFactory.Instance.CreateLogger("FakeTokenHandler");

        var tokenHandler = new FakeTokenHandler(_scopeFactory, logger, authenticator);

        tokenHandler.InnerHandler = new MockHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsoluteUri == "https://example.com/token")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"access_token":"valid-access-token","expires_in":3600}""",
                        Encoding.UTF8,
                        "application/json")
                };
            }

            Assert.Equal("Bearer valid-access-token", request.Headers.Authorization?.ToString());

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"data":"ok"}""", Encoding.UTF8, "application/json")
            };
        });

        using var client = new HttpClient(tokenHandler);

        var response = await client.GetAsync("https://example.com/api/resource");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, authenticator.CallCount);
    }

    [Fact]
    public async Task SendAsync_NoRefreshToken_TriggersReauth()
    {
        var authenticator = new MockAuthenticator(_db);
        var logger = NullLoggerFactory.Instance.CreateLogger("FakeTokenHandler");

        var tokenHandler = new FakeTokenHandler(_scopeFactory, logger, authenticator);

        var refreshAttempts = 0;
        tokenHandler.InnerHandler = new MockHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsoluteUri == "https://example.com/token")
            {
                refreshAttempts++;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"access_token":"access-from-reauth","expires_in":3600}""",
                        Encoding.UTF8,
                        "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"data":"ok"}""", Encoding.UTF8, "application/json")
            };
        });

        using var client = new HttpClient(tokenHandler);

        var response = await client.GetAsync("https://example.com/api/resource");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, authenticator.CallCount);
        Assert.Contains(_db.RefreshTokens, t => t.Token == "fresh-refresh-token");
    }
}
