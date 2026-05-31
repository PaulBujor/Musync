using System.Runtime.CompilerServices;
using SpotifyTools.Domain;
using SpotifyTools.Domain.Interfaces;

namespace SpotifyTools.Tests.Fakes;

public sealed class LocalMockMusicProvider : IMusicProvider
{
    private readonly List<Album> _savedAlbums;
    private readonly HashSet<string> _likedTrackIds;
    private List<Track> _playlistTracks;

    public IReadOnlyList<Track> PlaylistTracks => _playlistTracks.AsReadOnly();

    public LocalMockMusicProvider(
        List<Album>? savedAlbums = null,
        HashSet<string>? likedTrackIds = null,
        List<Track>? playlistTracks = null)
    {
        _savedAlbums = savedAlbums ?? [];
        _likedTrackIds = likedTrackIds ?? [];
        _playlistTracks = playlistTracks ?? [];
    }

    public async IAsyncEnumerable<Album> GetSavedAlbumsAsync([EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var album in _savedAlbums)
        {
            if (ct.IsCancellationRequested) yield break;
            yield return album;
        }
    }

    public async IAsyncEnumerable<Track> GetAlbumTracksAsync(string albumId, [EnumeratorCancellation] CancellationToken ct)
    {
        var album = _savedAlbums.FirstOrDefault(a => a.Id == albumId);
        if (album != null)
        {
            foreach (var track in AlbumTracks(albumId))
            {
                if (ct.IsCancellationRequested) yield break;
                yield return track;
            }
        }
    }

    public async IAsyncEnumerable<Track> GetPlaylistTracksAsync(string playlistId, [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var track in _playlistTracks)
        {
            if (ct.IsCancellationRequested) yield break;
            yield return track;
        }
    }

    public Task<HashSet<string>> GetLikedTrackIdsAsync(CancellationToken ct)
    {
        return Task.FromResult(new HashSet<string>(_likedTrackIds));
    }

    public Task AddTracksToPlaylistAsync(string playlistId, IEnumerable<string> trackUris, CancellationToken ct)
    {
        var newTracks = trackUris.Select(uri => new Track(uri, "", "", ""));
        _playlistTracks.AddRange(newTracks);
        return Task.CompletedTask;
    }

    public Task RemoveTracksFromPlaylistAsync(string playlistId, IEnumerable<string> trackUris, CancellationToken ct)
    {
        var toRemove = trackUris.ToHashSet();
        _playlistTracks = _playlistTracks.Where(t => !toRemove.Contains(t.Id)).ToList();
        return Task.CompletedTask;
    }

    private List<Track> AlbumTracks(string albumId)
    {
        return albumId switch
        {
            "album-a" =>
            [
                new Track("track-a1", "Track A1", "Artist A", "Album A"),
                new Track("track-a2", "Track A2", "Artist A", "Album A"),
            ],
            "album-b" =>
            [
                new Track("track-b1", "Track B1", "Artist B", "Album B"),
                new Track("track-b2", "Track B2", "Artist B", "Album B"),
            ],
            "album-c" =>
            [
                new Track("track-c1", "Track C1", "Artist C", "Album C"),
            ],
            _ => []
        };
    }
}
