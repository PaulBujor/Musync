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

    protected override string AuthUrl => _options.AuthUrl;
    protected override string TokenUrl => _options.TokenUrl;
    protected override string ProviderName => "tidal";
    protected override string Scope => _options.Scopes;
    protected override string ClientId => _options.ClientId;
    protected override string RedirectUri => _options.RedirectUri;
}
