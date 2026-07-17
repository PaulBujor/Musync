using System.Runtime.CompilerServices;
using Musync.Domain;
using Musync.Domain.Interfaces;

namespace Musync.Tests.Fakes;

public sealed class LocalMockMusicProvider(
    List<Album>? savedAlbums = null,
    List<Track>? playlistTracks = null,
    List<Track>? savedTracks = null,
    Dictionary<string, List<Track>>? albumTracks = null)
    : IMusicProvider
{
    private readonly Dictionary<string, List<Track>>? _albumTracksOverride = albumTracks;
    private readonly List<Album> _savedAlbums = savedAlbums ?? [];
    private readonly List<Track> _savedTracks = savedTracks ?? [];
    private List<Track> _playlistTracks = playlistTracks ?? [];

    public IReadOnlyList<Track> PlaylistTracks => _playlistTracks.AsReadOnly();

    public async IAsyncEnumerable<Album> GetSavedAlbumsAsync([EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var album in _savedAlbums)
        {
            if (ct.IsCancellationRequested) yield break;
            yield return album;
        }
    }

    public async IAsyncEnumerable<Track> GetAlbumTracksAsync(string albumId, string albumName,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var album = _savedAlbums.FirstOrDefault(a => a.Id == albumId);
        if (album != null)
            foreach (var track in AlbumTracks(albumId, albumName))
            {
                if (ct.IsCancellationRequested) yield break;
                yield return track;
            }
    }

    public async IAsyncEnumerable<Track> GetSavedTracksAsync([EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var track in _savedTracks)
        {
            if (ct.IsCancellationRequested) yield break;
            yield return track;
        }
    }

    public async IAsyncEnumerable<Track> GetPlaylistTracksAsync(string playlistId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var track in _playlistTracks)
        {
            if (ct.IsCancellationRequested) yield break;
            yield return track;
        }
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

    private List<Track> AlbumTracks(string albumId, string albumName)
    {
        if (_albumTracksOverride is not null)
            return _albumTracksOverride.TryGetValue(albumId, out var tracks) ? tracks : [];

        return albumId switch
        {
            "album-a" =>
            [
                new Track("track-a1", "Track A1", "Artist A", albumName),
                new Track("track-a2", "Track A2", "Artist A", albumName)
            ],
            "album-b" =>
            [
                new Track("track-b1", "Track B1", "Artist B", albumName),
                new Track("track-b2", "Track B2", "Artist B", albumName)
            ],
            "album-c" =>
            [
                new Track("track-c1", "Track C1", "Artist C", albumName)
            ],
            _ => []
        };
    }
}