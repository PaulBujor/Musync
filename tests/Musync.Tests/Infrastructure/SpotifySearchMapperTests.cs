using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Musync.Domain;
using Musync.Infrastructure.Spotify;
using Musync.Tests.Fakes;

namespace Musync.Tests.Infrastructure;

public sealed class SpotifySearchMapperTests
{
    private static SpotifySearchMapper CreateMapper(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        var http = new HttpClient(new MockHttpMessageHandler(handler))
        {
            BaseAddress = new Uri("https://api.spotify.com/v1/")
        };
        return new SpotifySearchMapper(http, NullLogger<SpotifySearchMapper>.Instance);
    }

    private static HttpResponseMessage TracksResponse(params (string Id, string? Isrc)[] items)
    {
        var itemJson = items.Select(i => i.Isrc is null
            ? $"{{\"id\":\"{i.Id}\"}}"
            : $"{{\"id\":\"{i.Id}\",\"external_ids\":{{\"isrc\":\"{i.Isrc}\"}}}}");
        var body = $"{{\"tracks\":{{\"items\":[{string.Join(",", itemJson)}]}}}}";

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }

    private static bool IsIsrcQuery(HttpRequestMessage request)
        => request.RequestUri!.Query.Contains("isrc", StringComparison.OrdinalIgnoreCase);

    [Fact]
    public async Task IsrcMatch_ReturnsAuthoritativeHit()
    {
        var mapper = CreateMapper(request =>
            IsIsrcQuery(request)
                ? TracksResponse(("spotify-isrc", "USRC10000001"))
                : throw new InvalidOperationException("fallback should not run when the isrc search hits"));

        var result = await mapper.FindMatchAsync(
            new Track("src", "Song", "Artist", "Album", "USRC10000001"), CancellationToken.None);

        Assert.Equal(TrackMatchOutcome.Matched, result.Outcome);
        Assert.Equal("spotify-isrc", result.TargetTrackId);
    }

    [Fact]
    public async Task Fallback_NoIsrcOnSource_ReturnsTopHit()
    {
        var mapper = CreateMapper(_ => TracksResponse(("top", "USRCZZ"), ("second", "USRCYY")));

        var result = await mapper.FindMatchAsync(
            new Track("src", "Song", "Artist", "Album"), CancellationToken.None);

        Assert.Equal(TrackMatchOutcome.Matched, result.Outcome);
        Assert.Equal("top", result.TargetTrackId);
    }

    [Fact]
    public async Task Fallback_IsrcOnSource_PrefersIsrcMatchOverTopHit()
    {
        var mapper = CreateMapper(request =>
            IsIsrcQuery(request)
                ? TracksResponse() // isrc: search finds nothing
                : TracksResponse(("wrong", "USRCOTHER"), ("right", "usrc10000001"))); // lowercase → still matches

        var result = await mapper.FindMatchAsync(
            new Track("src", "Song", "Artist", "Album", "USRC10000001"), CancellationToken.None);

        Assert.Equal(TrackMatchOutcome.Matched, result.Outcome);
        Assert.Equal("right", result.TargetTrackId);
    }

    [Fact]
    public async Task Fallback_IsrcOnSource_NoCandidateMatches_ReturnsNotFound()
    {
        var mapper = CreateMapper(request =>
            IsIsrcQuery(request)
                ? TracksResponse()
                : TracksResponse(("wrong-1", "USRCAAA"), ("wrong-2", "USRCBBB")));

        var result = await mapper.FindMatchAsync(
            new Track("src", "Song", "Artist", "Album", "USRC10000001"), CancellationToken.None);

        Assert.Equal(TrackMatchOutcome.NotFound, result.Outcome);
        Assert.Null(result.TargetTrackId);
    }

    [Fact]
    public async Task SearchRequestFails_ReturnsSearchFailed_NotNotFound()
    {
        var mapper = CreateMapper(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var result = await mapper.FindMatchAsync(
            new Track("src", "Song", "Artist", "Album", "USRC10000001"), CancellationToken.None);

        // A transient failure must NOT be reported as a genuine no-match (which would be cached).
        Assert.Equal(TrackMatchOutcome.SearchFailed, result.Outcome);
    }
}
