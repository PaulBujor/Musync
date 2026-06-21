using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpotifyTools.Domain.Interfaces;
using SpotifyTools.Infrastructure.Auth;
using SpotifyTools.Infrastructure.Persistence;
using SpotifyTools.Options;

namespace SpotifyTools.Infrastructure.Spotify;

public sealed class SpotifyAuthenticator(
    IOptions<SpotifyOptions> options,
    SpotifyDbContext db,
    ILogger<SpotifyAuthenticator> logger,
    HttpClient httpClient)
    : PkceAuthenticatorBase(db, logger, httpClient), ISpotifyAuthenticator
{
    private readonly SpotifyOptions _options = options.Value;

    protected override string AuthUrl => _options.AuthUrl;
    protected override string TokenUrl => _options.TokenUrl;
    protected override string ProviderName => "spotify";
    protected override string Scope => _options.Scopes;
    protected override string ClientId => _options.ClientId;
    protected override string RedirectUri => _options.RedirectUri;
}
