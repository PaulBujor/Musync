namespace Musync.Domain;

public sealed record Track(
    string Id,
    string Name,
    string Artist,
    string Album,
    string? Isrc = null,
    DateTimeOffset? AddedAt = null)
{
    // Keys used for de-duplication. Two tracks are treated as the same song when they share ANY of
    // these: the provider catalog id, the ISRC, or the normalized artist+title. The name key catches
    // the same recording released under different catalog ids AND different ISRCs (separate album
    // pressings/masters), which id- and ISRC-matching alone miss.
    public string IdKey => $"id:{Id}";

    public string? IsrcKey =>
        string.IsNullOrWhiteSpace(Isrc) ? null : $"isrc:{Isrc.Trim().ToUpperInvariant()}";

    public string? NameKey =>
        string.IsNullOrWhiteSpace(Artist) || string.IsNullOrWhiteSpace(Name)
            ? null
            : $"name:{Normalize(Artist)}{Normalize(Name)}";

    // All identity keys this track carries, most-specific-song-identity first.
    public IEnumerable<string> DedupKeys()
    {
        if (NameKey is { } n) yield return n;
        if (IsrcKey is { } i) yield return i;
        yield return IdKey;
    }

    // A single representative identity for counting distinct songs (name preferred, then ISRC, id).
    public string PrimaryKey => NameKey ?? IsrcKey ?? IdKey;

    private static string Normalize(string value) =>
        string.Join(' ', value.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
