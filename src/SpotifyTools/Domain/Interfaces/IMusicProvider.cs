namespace SpotifyTools.Domain.Interfaces;

public interface IMusicProvider
{
    IAsyncEnumerable<Album> GetSavedAlbumsAsync(CancellationToken ct);
    IAsyncEnumerable<Track> GetAlbumTracksAsync(string albumId, string albumName, CancellationToken ct);
    IAsyncEnumerable<Track> GetPlaylistTracksAsync(string playlistId, CancellationToken ct);
    Task<HashSet<string>> GetLikedTrackIdsAsync(CancellationToken ct);
    Task AddTracksToPlaylistAsync(string playlistId, IEnumerable<string> trackUris, CancellationToken ct);
    Task RemoveTracksFromPlaylistAsync(string playlistId, IEnumerable<string> trackUris, CancellationToken ct);
}