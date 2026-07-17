namespace Musync.Jobs;

public static class CacheKeys
{
    public static string LikedTracks(string provider)
    {
        return $"{provider}:liked-tracks";
    }
}