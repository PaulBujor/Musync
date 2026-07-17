using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Musync.Domain.Interfaces;
using Musync.Infrastructure.Auth;
using Musync.Infrastructure.Persistence;
using Musync.Options;

namespace Musync.Infrastructure.Tidal;

public sealed class TidalAuthenticator(
    IOptions<TidalOptions> options,
    AppDbContext db,
    ILogger<TidalAuthenticator> logger,
    HttpClient httpClient)
    : PkceAuthenticatorBase(db, logger, httpClient), ITidalAuthenticator
{
    private readonly TidalOptions _options = options.Value;

    protected override string AuthUrl => _options.AuthUrl;
    protected override string TokenUrl => _options.TokenUrl;
    protected override string ProviderName => ProviderKeys.Tidal;
    protected override string Scope => _options.Scopes;
    protected override string ClientId => _options.ClientId;
    protected override string RedirectUri => _options.RedirectUri;
}
