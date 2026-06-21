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
    private static readonly string[] Scopes =
    [
        "user-library-read",
        "playlist-modify-public",
        "playlist-modify-private",
        "playlist-read-private",
        "playlist-read-collaborative"
    ];

    private readonly SpotifyOptions _options = options.Value;

    protected override string AuthUrl => "https://accounts.spotify.com/authorize";
    protected override string TokenUrl => "https://accounts.spotify.com/api/token";
    protected override string ProviderName => "spotify";
    protected override string Scope => string.Join(" ", Scopes);
    protected override string ClientId => _options.ClientId;
    protected override string RedirectUri => _options.RedirectUri;
}
