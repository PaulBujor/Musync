namespace Musync.Options;

// Tidal is import-source only; it needs no queue playlist or write concurrency knobs.
public sealed record TidalOptions : ProviderOptionsBase
{
    // Sent as the `locale` query parameter on v2 collection/catalog reads (e.g. "en-US").
    public string Locale { get; init; } = "en-US";
}
