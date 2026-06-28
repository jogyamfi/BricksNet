using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using BricksNet.Contracts.Options;
using Microsoft.Extensions.Logging;

namespace BricksNet.Connection.Handlers;

/// <summary>
/// Delegating handler that retries transient HTTP failures (<c>429 Too Many Requests</c> and
/// <c>503 Service Unavailable</c>) using exponential backoff with full jitter.
/// </summary>
/// <remarks>
/// Backoff follows the model in the Node.js driver
/// (<c>databricks-sql-nodejs/lib/connection/connections/HttpRetryPolicy.ts</c>): the per-attempt
/// ceiling is <c>min(DelayMaxMs, DelayMinMs * 2^attempt)</c>, and the actual delay is a uniform
/// random value in <c>[0, ceiling]</c> (full jitter). The <c>Retry-After</c> response header, when
/// present and valid, raises the floor of the backoff window. Jitter is drawn from a cryptographic
/// RNG and waits use <see cref="Task.Delay(TimeSpan, CancellationToken)"/> — never a blocking sleep.
/// </remarks>
internal sealed class HttpRetryHandler : DelegatingHandler
{
    private readonly RetryOptions _options;
    private readonly ILogger<HttpRetryHandler> _logger;

    public HttpRetryHandler(RetryOptions options, ILogger<HttpRetryHandler> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // .NET does not allow re-sending the same HttpRequestMessage. Buffer the body (if any)
        // before the first send so we can construct a fresh clone for each retry attempt.
        (byte[] Bytes, System.Net.Http.Headers.HttpContentHeaders Headers)? bufferedContent = null;
        if (request.Content is not null)
        {
            byte[] bytes = await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            // Capture content headers (Content-Type, Content-Encoding, …) before the original is disposed.
            var tempContent = new ByteArrayContent(bytes);
            foreach (System.Collections.Generic.KeyValuePair<string, System.Collections.Generic.IEnumerable<string>> h
                     in request.Content.Headers)
            {
                tempContent.Headers.TryAddWithoutValidation(h.Key, h.Value);
            }
            request.Content = tempContent;
            bufferedContent = (bytes, tempContent.Headers);
        }

        long startTimestamp = Stopwatch.GetTimestamp();
        int attempt = 0;
        HttpResponseMessage response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        while (IsTransient(response.StatusCode))
        {
            attempt++;

            TimeSpan elapsed = Stopwatch.GetElapsedTime(startTimestamp);
            if (attempt >= _options.MaxAttempts || elapsed.TotalMilliseconds >= _options.TimeoutMs)
            {
                _logger.LogWarning(
                    "Retries exhausted for {Method} {Uri} after {Attempts} attempt(s) ({Elapsed}); returning {StatusCode}.",
                    request.Method,
                    request.RequestUri,
                    attempt,
                    elapsed,
                    (int)response.StatusCode);
                return response;
            }

            TimeSpan delay = ComputeDelay(attempt, response);

            // Don't wait past the overall retry budget.
            double remainingMs = _options.TimeoutMs - elapsed.TotalMilliseconds;
            if (delay.TotalMilliseconds > remainingMs)
            {
                _logger.LogWarning(
                    "Retry budget exhausted for {Method} {Uri} after {Attempts} attempt(s); returning {StatusCode}.",
                    request.Method,
                    request.RequestUri,
                    attempt,
                    (int)response.StatusCode);
                return response;
            }

            _logger.LogDebug(
                "Retrying {Method} {Uri} (attempt {Attempt}/{MaxAttempts}) after {Delay} due to {StatusCode}.",
                request.Method,
                request.RequestUri,
                attempt,
                _options.MaxAttempts,
                delay,
                (int)response.StatusCode);

            response.Dispose();
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);

            // Each retry must use a fresh HttpRequestMessage — .NET forbids re-sending a used one.
            using HttpRequestMessage retryRequest = CloneRequest(request, bufferedContent);
            response = await base.SendAsync(retryRequest, cancellationToken).ConfigureAwait(false);
        }

        return response;
    }

    private static HttpRequestMessage CloneRequest(
        HttpRequestMessage original,
        (byte[] Bytes, System.Net.Http.Headers.HttpContentHeaders Headers)? bufferedContent)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri)
        {
            Version = original.Version,
        };

        foreach (System.Collections.Generic.KeyValuePair<string, System.Collections.Generic.IEnumerable<string>> h
                 in original.Headers)
        {
            clone.Headers.TryAddWithoutValidation(h.Key, h.Value);
        }

        foreach (System.Collections.Generic.KeyValuePair<string, object?> opt in original.Options)
        {
            clone.Options.TryAdd(opt.Key, opt.Value);
        }

        if (bufferedContent is { } bc)
        {
            var content = new ByteArrayContent(bc.Bytes);
            foreach (System.Collections.Generic.KeyValuePair<string, System.Collections.Generic.IEnumerable<string>> h
                     in bc.Headers)
            {
                content.Headers.TryAddWithoutValidation(h.Key, h.Value);
            }
            clone.Content = content;
        }

        return clone;
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable;

    private TimeSpan ComputeDelay(int attempt, HttpResponseMessage response)
    {
        // Per-attempt ceiling: min(DelayMaxMs, DelayMinMs * 2^attempt), guarding against overflow.
        double exponential = _options.DelayMinMs * Math.Pow(2, attempt);
        double ceilingMs = Math.Min(_options.DelayMaxMs, exponential);

        // The Retry-After header, when valid, raises the floor of the backoff window.
        double floorMs = GetRetryAfterMs(response) ?? 0;
        floorMs = Math.Min(floorMs, ceilingMs);

        // Full jitter: uniform random value in [floor, ceiling] drawn from a cryptographic RNG.
        double range = ceilingMs - floorMs;
        double jitter = range <= 0 ? 0 : RandomNumberGenerator.GetInt32(0, (int)range + 1);
        return TimeSpan.FromMilliseconds(floorMs + jitter);
    }

    private static double? GetRetryAfterMs(HttpResponseMessage response)
    {
        RetryConditionHeaderValue? retryAfter = response.Headers.RetryAfter;
        if (retryAfter is null)
        {
            return null;
        }

        if (retryAfter.Delta is { } delta)
        {
            return delta.TotalMilliseconds;
        }

        if (retryAfter.Date is { } date)
        {
            double ms = (date - DateTimeOffset.UtcNow).TotalMilliseconds;
            return ms > 0 ? ms : null;
        }

        return null;
    }
}
