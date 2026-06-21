namespace SpotifyTools.Options;

public sealed record TidalOptions : ProviderOptionsBase
{
    public string ClientSecret { get; init; } = "";
}
