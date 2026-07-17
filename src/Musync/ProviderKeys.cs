namespace Musync;

/// <summary>
///     Canonical provider identifiers — used as keyed-service keys, CLI command names, and the
///     <c>ProviderName</c> stored on tokens/history. Distinct from the PascalCase config section names.
/// </summary>
public static class ProviderKeys
{
    public const string Spotify = "spotify";
    public const string Tidal = "tidal";

    public static readonly string[] All = [Spotify, Tidal];
}