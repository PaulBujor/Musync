using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpotifyTools.Domain.Interfaces;
using SpotifyTools.Infrastructure.Auth;
using SpotifyTools.Infrastructure.Persistence;
using SpotifyTools.Options;

namespace SpotifyTools.Infrastructure.Tidal;

public sealed class TidalAuthenticator(
    IOptions<TidalOptions> options,
    SpotifyDbContext db,
    ILogger<TidalAuthenticator> logger,
    HttpClient httpClient)
    : PkceAuthenticatorBase(db, logger, httpClient), ITidalAuthenticator
{
    private readonly TidalOptions _options = options.Value;

    protected override string AuthUrl => "https://login.tidal.com/authorize";
    protected override string TokenUrl => "https://auth.tidal.com/v1/oauth2/token";
    protected override string ProviderName => "tidal";
    protected override string Scope => "user.read user_collection.read";
    protected override string ClientId => _options.ClientId;
    protected override string RedirectUri => _options.RedirectUri;
}
