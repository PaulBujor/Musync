using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Musync.Domain.Interfaces;
using Musync.Infrastructure.Auth;
using Musync.Infrastructure.Persistence;
using Musync.Options;

namespace Musync.Infrastructure.Spotify;

public sealed class SpotifyAuthenticator(
    IOptions<SpotifyOptions> options,
    AppDbContext db,
    ILogger<SpotifyAuthenticator> logger,
    HttpClient httpClient)
    : PkceAuthenticatorBase(db, logger, httpClient), ISpotifyAuthenticator
{
    private readonly SpotifyOptions _options = options.Value;

    protected override string AuthUrl => _options.AuthUrl;
    protected override string TokenUrl => _options.TokenUrl;
    protected override string ProviderName => ProviderKeys.Spotify;
    protected override string Scope => _options.Scopes;
    protected override string ClientId => _options.ClientId;
    protected override string RedirectUri => _options.RedirectUri;
}