using System.Net;
using System.Text;
using Musync.Domain;
using Musync.Infrastructure.Tidal;
using Musync.Tests.Fakes;

namespace Musync.Tests.Infrastructure;

public sealed class TidalMusicProviderTests
{
    private static TidalMusicProvider CreateProvider(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        var http = new HttpClient(new MockHttpMessageHandler(handler))
        {
            BaseAddress = new Uri("https://openapi.tidal.com/v2/")
        };
        return new TidalMusicProvider(http, "en-US");
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

        // The /v2 base segment is preserved and the query carries locale + nested include.
        Assert.Equal("/v2/userCollectionTracks/me", requestedUri!.AbsolutePath);
        Assert.Contains("locale=en-US", requestedUri.Query);
        Assert.Contains("include=items.artists,items.albums", Uri.UnescapeDataString(requestedUri.Query));
    }

    [Fact]
    public async Task GetSavedTracksAsync_FollowsNextLinkPagination()
    {
        const string page1 =
            """
            {
              "included": [
                { "type": "tracks", "id": "t1", "attributes": { "title": "One" },
                  "relationships": { "artists": { "data": [ { "type": "artists", "id": "a1" } ] } } },
                { "type": "artists", "id": "a1", "attributes": { "name": "Artist One" } }
              ],
              "links": { "next": "https://openapi.tidal.com/v2/userCollectionTracks/me?page%5Bcursor%5D=next" }
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

        var provider = CreateProvider(request =>
            Json(request.RequestUri!.Query.Contains("cursor") ? page2 : page1));

        var tracks = await CollectAsync(provider);

        Assert.Equal(2, tracks.Count);
        Assert.Equal("t1", tracks[0].Id);
        Assert.Equal("Artist One", tracks[0].Artist);
        Assert.Equal("t2", tracks[1].Id);
        Assert.Equal("Artist Two", tracks[1].Artist);
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
}
