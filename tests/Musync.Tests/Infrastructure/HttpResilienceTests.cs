using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Musync.Infrastructure.Http;

namespace Musync.Tests.Infrastructure;

// Drives the real resilience pipeline (AddStandardResilienceHandler) through a scripted handler to
// pin the retry contracts: 429 is retried everywhere (and kept out of the breaker), but non-idempotent
// writes never retry 5xx/network/timeout — only 429, which the server rejected before processing.
public sealed class HttpResilienceTests
{
    [Fact]
    public async Task Write_RetriesOn429_ThenSucceeds()
    {
        var handler = new SequenceHandler(HttpStatusCode.TooManyRequests, HttpStatusCode.OK);
        var client = BuildClient(handler, o => HttpResilience.ConfigureWrite(o, 3, "Test", () => null));

        var response = await client.PostAsync("items", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, handler.CallCount); // one 429, then retried to success
    }

    [Fact]
    public async Task Write_DoesNotRetryServerErrors()
    {
        // 500 could mean the write committed but the response was lost — retrying a non-idempotent
        // write would duplicate. So it must NOT be retried (the 500 surfaces after a single attempt).
        var handler = new SequenceHandler(HttpStatusCode.InternalServerError, HttpStatusCode.OK);
        var client = BuildClient(handler, o => HttpResilience.ConfigureWrite(o, 3, "Test", () => null));

        var response = await client.PostAsync("items", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Read_RetriesServerErrors_ThenSucceeds()
    {
        // Reads are idempotent, so the shared config still retries every transient failure.
        var handler = new SequenceHandler(HttpStatusCode.InternalServerError, HttpStatusCode.OK);
        var client = BuildClient(handler, o => HttpResilience.ConfigureRead(o, 3, "Test", () => null));

        var response = await client.GetAsync("items");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, handler.CallCount);
    }

    private static HttpClient BuildClient(HttpMessageHandler primary, Action<HttpStandardResilienceOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddHttpClient("test")
            .ConfigurePrimaryHttpMessageHandler(() => primary)
            .AddStandardResilienceHandler(configure);

        var client = services.BuildServiceProvider()
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient("test");
        client.BaseAddress = new Uri("https://example.test/");
        return client;
    }

    // Returns the scripted statuses in order (repeating the last). Non-success responses carry
    // Retry-After: 0 so retry delays are instant, keeping the tests fast.
    private sealed class SequenceHandler(params HttpStatusCode[] statuses) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var status = statuses[Math.Min(CallCount, statuses.Length - 1)];
            CallCount++;

            var response = new HttpResponseMessage(status);
            if ((int)status >= 300)
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
            return Task.FromResult(response);
        }
    }
}
