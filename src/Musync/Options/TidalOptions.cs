namespace Musync.Options;

// Tidal is import-source only; it needs no queue playlist or write concurrency knobs.
public sealed record TidalOptions : ProviderOptionsBase;
