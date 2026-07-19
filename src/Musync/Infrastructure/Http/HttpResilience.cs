using System.Net;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Musync.Jobs;

namespace Musync.Infrastructure.Http;

/// <summary>
///     Resilience configuration shared by the read and write HTTP clients. Both keep 429 out of the
///     circuit breaker and honour <c>Retry-After</c>; the difference is what gets retried — reads retry
///     every transient failure, writes (non-idempotent) retry only 429.
/// </summary>
public static class HttpResilience
{
    // Reads: retry every transient failure (the standard handler's default predicate — 5xx/408/429/network),
    // honouring Retry-After.
    public static void ConfigureRead(
        HttpStandardResilienceOptions options, int maxRetries, string provider, Func<ILogger?> logger)
        => ConfigureRateLimitAware(options, maxRetries, provider, logger);

    // Writes (playlist add/remove) are non-idempotent: a retried request that actually committed but lost
    // its response would duplicate tracks. So retry ONLY on 429 — the server rejected it before processing,
    // so nothing was applied and a Retry-After backoff is safe. 5xx/network/timeout are never retried.
    public static void ConfigureWrite(
        HttpStandardResilienceOptions options, int maxRetries, string provider, Func<ILogger?> logger)
    {
        ConfigureRateLimitAware(options, maxRetries, provider, logger);
        options.Retry.ShouldHandle = args =>
            ValueTask.FromResult(args.Outcome.Result?.StatusCode == HttpStatusCode.TooManyRequests);
    }

    // Shared base: rate-limit-aware retry that honours Retry-After and keeps 429 out of the circuit
    // breaker. No practical overall timeout (1 day) so a long rate-limit backoff can finish — the
    // standard handler's validator rejects an infinite total against a finite attempt, so this is a
    // large finite value. Attempts stay bounded, and Ctrl+C cancels (token flows into the delay).
    private static void ConfigureRateLimitAware(
        HttpStandardResilienceOptions options, int maxRetries, string provider, Func<ILogger?> logger)
    {
        options.TotalRequestTimeout.Timeout = TimeSpan.FromDays(1);
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(30);
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(60);
        ExcludeRateLimitFromCircuitBreaker(options);
        options.Retry.MaxRetryAttempts = maxRetries;
        options.Retry.DelayGenerator = args =>
            ValueTask.FromResult(args.Outcome.Result?.Headers.RetryAfter?.Delta);
        options.Retry.OnRetry = args =>
        {
            if (args.Outcome.Result is { StatusCode: HttpStatusCode.TooManyRequests } response
                && logger() is { } log)
            {
                var delay = response.Headers.RetryAfter?.Delta ?? args.RetryDelay;
                Log.RateLimited(log, provider, args.AttemptNumber + 1, delay.TotalSeconds);
            }

            return default;
        };
    }

    // 429 is a rate-limit signal we handle via the retry strategy (honouring Retry-After), not a
    // service-health failure. It must not count toward the circuit breaker, or a single 429 opens the
    // breaker and the retry's next attempt immediately hits an open circuit and fails the whole run.
    // The breaker still trips on the transient failures we DON'T handle (5xx, 408, network, timeout) —
    // this wraps the standard handler's default predicate rather than replacing it.
    private static void ExcludeRateLimitFromCircuitBreaker(HttpStandardResilienceOptions options)
    {
        var defaultShouldHandle = options.CircuitBreaker.ShouldHandle;
        options.CircuitBreaker.ShouldHandle = async args =>
            args.Outcome.Result?.StatusCode != HttpStatusCode.TooManyRequests
            && await defaultShouldHandle(args);
    }
}
