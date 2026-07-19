using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Musync.Domain;
using Musync.Infrastructure.Tidal;
using Musync.Tests.Fakes;

namespace Musync.Tests.Infrastructure;

public sealed class TidalMusicProviderTests
{
    private static TidalMusicProvider CreateProvider(
        Func<HttpRequestMessage, HttpResponseMessage> handler,
        Func<HttpRequestMessage, HttpResponseMessage>? writeHandler = null)
    {
        static HttpClient Client(Func<HttpRequestMessage, HttpResponseMessage> h) =>
            new(new MockHttpMessageHandler(h)) { BaseAddress = new Uri("https://openapi.tidal.com/v2/") };

        // Read tests never touch the write client; default it to the read handler.
        return new TidalMusicProvider(Client(handler), Client(writeHandler ?? handler), "en-US");
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/vnd.api+json")
        };

    private static async Task<List<Track>> CollectAsync(TidalMusicProvider provider)
    {
        var tracks = new List<Track>();
        await foreach (var track in provider.GetSavedTracksAsync(CancellationToken.None))
            tracks.Add(track);
        return tracks;
    }

    [Fact]
    public async Task GetSavedTracksAsync_ResolvesTracksArtistsAndAlbumsFromIncluded()
    {
        const string body =
            """
            {
              "included": [
                { "type": "tracks", "id": "t1",
                  "attributes": { "title": "Song One", "isrc": "US0000000001" },
                  "relationships": {
                    "artists": { "data": [ { "type": "artists", "id": "a1" } ] },
                    "albums":  { "data": [ { "type": "albums",  "id": "al1" } ] } } },
                { "type": "artists", "id": "a1", "attributes": { "name": "Artist One" } },
                { "type": "albums",  "id": "al1", "attributes": { "title": "Album One" } }
              ],
              "links": { "next": null }
            }
            """;

        Uri? requestedUri = null;
        var provider = CreateProvider(request =>
        {
            requestedUri = request.RequestUri;
            return Json(body);
        });

        var tracks = await CollectAsync(provider);

        var track = Assert.Single(tracks);
        Assert.Equal("t1", track.Id);
        Assert.Equal("Song One", track.Name);
        Assert.Equal("Artist One", track.Artist);
        Assert.Equal("Album One", track.Album);
        Assert.Equal("US0000000001", track.Isrc);

        // The /v2 base segment is preserved and the query carries locale + nested include. The
        // paginating sub-resource (…/relationships/items), not the singular …/me collection, is
        // what carries a top-level links.next — see GetSavedTracksAsync_FollowsNextLinkPagination.
        Assert.Equal("/v2/userCollectionTracks/me/relationships/items", requestedUri!.AbsolutePath);
        Assert.Contains("locale=en-US", requestedUri.Query);
        Assert.Contains("include=items.artists,items.albums", Uri.UnescapeDataString(requestedUri.Query));
    }

    [Fact]
    public async Task GetSavedTracksAsync_FollowsNextLinkPagination()
    {
        // TIDAL returns links.next as a leading-slash path WITHOUT the /v2 version segment
        // (e.g. "/userCollectionTracks/…"). Passed verbatim to HttpClient with a ".../v2/" base,
        // the leading slash resolves against the host root and drops /v2 → 404. The provider must
        // re-root it under /v2, so page 2 is fetched at /v2/userCollectionTracks/…
        const string page1 =
            """
            {
              "included": [
                { "type": "tracks", "id": "t1", "attributes": { "title": "One" },
                  "relationships": { "artists": { "data": [ { "type": "artists", "id": "a1" } ] } } },
                { "type": "artists", "id": "a1", "attributes": { "name": "Artist One" } }
              ],
              "links": { "next": "/userCollectionTracks/me/relationships/items?include=items.artists,items.albums&locale=en-US&page%5Bcursor%5D=20" }
            }
            """;
        const string page2 =
            """
            {
              "included": [
                { "type": "tracks", "id": "t2", "attributes": { "title": "Two" },
                  "relationships": { "artists": { "data": [ { "type": "artists", "id": "a2" } ] } } },
                { "type": "artists", "id": "a2", "attributes": { "name": "Artist Two" } }
              ],
              "links": { "next": null }
            }
            """;

        var requestedPaths = new List<string>();
        var provider = CreateProvider(request =>
        {
            requestedPaths.Add(request.RequestUri!.AbsolutePath);
            return Json(request.RequestUri!.Query.Contains("cursor") ? page2 : page1);
        });

        var tracks = await CollectAsync(provider);

        Assert.Equal(2, tracks.Count);
        Assert.Equal("t1", tracks[0].Id);
        Assert.Equal("Artist One", tracks[0].Artist);
        Assert.Equal("t2", tracks[1].Id);
        Assert.Equal("Artist Two", tracks[1].Artist);

        // Both pages hit the /v2-rooted path — the next link's missing version segment was restored.
        Assert.All(requestedPaths, p => Assert.Equal("/v2/userCollectionTracks/me/relationships/items", p));
    }

    [Fact]
    public async Task GetSavedTracksAsync_TrackMissingRelationships_YieldsWithEmptyArtistAndAlbum()
    {
        const string body =
            """
            {
              "included": [
                { "type": "tracks", "id": "t1", "attributes": { "title": "Lonely", "isrc": "US0000000002" } }
              ],
              "links": { "next": null }
            }
            """;

        var provider = CreateProvider(_ => Json(body));

        var track = Assert.Single(await CollectAsync(provider));
        Assert.Equal("Lonely", track.Name);
        Assert.Equal("", track.Artist);
        Assert.Equal("", track.Album);
        Assert.Equal("US0000000002", track.Isrc);
    }

    [Fact]
    public async Task GetSavedTracksAsync_EmptyCollection_YieldsNothing()
    {
        var provider = CreateProvider(_ => Json("""{ "included": [], "links": { "next": null } }"""));

        Assert.Empty(await CollectAsync(provider));
    }

    [Fact]
    public async Task GetSavedAlbumsAsync_ResolvesAlbumsAndArtistsFromIncluded()
    {
        const string body =
            """
            {
              "data": [ { "type": "albums", "id": "al1" } ],
              "included": [
                { "type": "albums", "id": "al1", "attributes": { "title": "Album One" },
                  "relationships": { "artists": { "data": [ { "type": "artists", "id": "ar1" } ] } } },
                { "type": "artists", "id": "ar1", "attributes": { "name": "Artist One" } }
              ],
              "links": { "next": null }
            }
            """;

        Uri? requestedUri = null;
        var provider = CreateProvider(request =>
        {
            requestedUri = request.RequestUri;
            return Json(body);
        });

        var albums = new List<Album>();
        await foreach (var a in provider.GetSavedAlbumsAsync(CancellationToken.None))
            albums.Add(a);

        var album = Assert.Single(albums);
        Assert.Equal("al1", album.Id);
        Assert.Equal("Album One", album.Name);
        Assert.Equal("Artist One", album.Artist);

        Assert.Equal("/v2/userCollectionAlbums/me/relationships/items", requestedUri!.AbsolutePath);
        Assert.Contains("include=items.artists", Uri.UnescapeDataString(requestedUri.Query));
    }

    [Fact]
    public async Task GetAlbumTracksAsync_UsesGivenAlbumNameAndResolvesArtist()
    {
        const string body =
            """
            {
              "data": [ { "type": "tracks", "id": "t1" } ],
              "included": [
                { "type": "tracks", "id": "t1", "attributes": { "title": "Song One", "isrc": "US0000000001" },
                  "relationships": { "artists": { "data": [ { "type": "artists", "id": "ar1" } ] } } },
                { "type": "artists", "id": "ar1", "attributes": { "name": "Artist One" } }
              ],
              "links": { "next": null }
            }
            """;

        Uri? requestedUri = null;
        var provider = CreateProvider(request =>
        {
            requestedUri = request.RequestUri;
            return Json(body);
        });

        var tracks = new List<Track>();
        await foreach (var t in provider.GetAlbumTracksAsync("al1", "Album One", CancellationToken.None))
            tracks.Add(t);

        var track = Assert.Single(tracks);
        Assert.Equal("t1", track.Id);
        Assert.Equal("Song One", track.Name);
        Assert.Equal("Artist One", track.Artist);
        // Album name comes from the caller, not an included albums resource.
        Assert.Equal("Album One", track.Album);
        Assert.Equal("US0000000001", track.Isrc);

        Assert.Equal("/v2/albums/al1/relationships/items", requestedUri!.AbsolutePath);
    }

    [Fact]
    public async Task GetPlaylistTracksAsync_ResolvesTracks()
    {
        const string body =
            """
            {
              "data": [ { "type": "tracks", "id": "t1" } ],
              "included": [
                { "type": "tracks", "id": "t1", "attributes": { "title": "Song One" },
                  "relationships": { "artists": { "data": [ { "type": "artists", "id": "ar1" } ] } } },
                { "type": "artists", "id": "ar1", "attributes": { "name": "Artist One" } }
              ],
              "links": { "next": null }
            }
            """;

        Uri? requestedUri = null;
        var provider = CreateProvider(request =>
        {
            requestedUri = request.RequestUri;
            return Json(body);
        });

        var tracks = new List<Track>();
        await foreach (var t in provider.GetPlaylistTracksAsync("pl1", CancellationToken.None))
            tracks.Add(t);

        var track = Assert.Single(tracks);
        Assert.Equal("t1", track.Id);
        Assert.Equal("Artist One", track.Artist);

        Assert.Equal("/v2/playlists/pl1/relationships/items", requestedUri!.AbsolutePath);
    }

    [Fact]
    public async Task AddTracksToPlaylistAsync_PostsBatchedJsonApiItems()
    {
        var writes = new List<(HttpMethod Method, string Path, string Body)>();
        var provider = CreateProvider(
            _ => Json("{}"),
            writeHandler: request =>
            {
                var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                writes.Add((request.Method, request.RequestUri!.AbsolutePath, body));
                return new HttpResponseMessage(HttpStatusCode.Created);
            });

        // 25 ids with a batch size of 20 → two requests (20 + 5).
        var ids = Enumerable.Range(1, 25).Select(i => $"t{i}").ToList();
        await provider.AddTracksToPlaylistAsync("pl1", ids, CancellationToken.None);

        Assert.Equal(2, writes.Count);
        Assert.All(writes, w =>
        {
            Assert.Equal(HttpMethod.Post, w.Method);
            Assert.Equal("/v2/playlists/pl1/relationships/items", w.Path);
        });
        // JSON:API body shape: { "data": [ { "id": "...", "type": "tracks" } ] }.
        Assert.Contains("\"data\":", writes[0].Body);
        Assert.Contains("\"type\":\"tracks\"", writes[0].Body);
        Assert.Contains("\"id\":\"t1\"", writes[0].Body);
        Assert.Equal(20, Regex.Count(writes[0].Body, "\"type\":\"tracks\""));
        Assert.Equal(5, Regex.Count(writes[1].Body, "\"type\":\"tracks\""));
    }

    [Fact]
    public async Task RemoveTracksFromPlaylistAsync_SendsBatchedDeleteJsonApiItems()
    {
        var writes = new List<(HttpMethod Method, string Path, string Body)>();
        var provider = CreateProvider(
            _ => Json("{}"),
            writeHandler: request =>
            {
                var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                writes.Add((request.Method, request.RequestUri!.AbsolutePath, body));
                return new HttpResponseMessage(HttpStatusCode.OK);
            });

        await provider.RemoveTracksFromPlaylistAsync("pl1", ["t1", "t2"], CancellationToken.None);

        var write = Assert.Single(writes);
        Assert.Equal(HttpMethod.Delete, write.Method);
        Assert.Equal("/v2/playlists/pl1/relationships/items", write.Path);
        Assert.Contains("\"id\":\"t1\"", write.Body);
        Assert.Contains("\"id\":\"t2\"", write.Body);
    }
}
