namespace Musync.Domain;

/// <summary>
/// Answers "is this song already present?" by provider track id, ISRC, OR normalized artist+title. A
/// track matches when ANY of its keys is already in the set: the same catalog id, the same recording
/// under a different id (ISRC), or the same artist+title released as a different pressing/master
/// (different id and ISRC). Keys are type-prefixed (<c>id:</c>/<c>isrc:</c>/<c>name:</c>) so values of
/// different kinds never collide in the one underlying set.
/// </summary>
public sealed class TrackPresenceSet
{
    private readonly HashSet<string> _keys = new(StringComparer.Ordinal);

    public bool Contains(Track track) => track.DedupKeys().Any(_keys.Contains);

    /// <summary>Adds a track's keys; returns true when it was not already present by any key.</summary>
    public bool Add(Track track)
    {
        var isNew = !Contains(track);
        foreach (var key in track.DedupKeys())
            _keys.Add(key);
        return isNew;
    }

    public void AddKeys(IEnumerable<string> keys)
    {
        foreach (var key in keys)
            _keys.Add(key);
    }
}

/// <summary>
/// Serializable snapshot of the liked/saved tracks, cached so the snapshot &amp; add steps share one
/// fetch. Holds every liked track's dedup keys (id / ISRC / artist-title) so a
/// <see cref="TrackPresenceSet"/> can be rebuilt from it.
/// </summary>
public sealed record LikedTracksIndex(HashSet<string> Keys)
{
    public static LikedTracksIndex FromTracks(IEnumerable<Track> tracks)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in tracks)
            foreach (var key in t.DedupKeys())
                keys.Add(key);

        return new LikedTracksIndex(keys);
    }

    public TrackPresenceSet ToPresenceSet()
    {
        var set = new TrackPresenceSet();
        set.AddKeys(Keys);
        return set;
    }
}
