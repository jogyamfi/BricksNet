using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BricksNet.Contracts.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BricksNet.Connection.Handlers;

public sealed class HttpRetryHandlerTests
{
    private static RetryOptions FastRetry(int maxAttempts = 5) => new()
    {
        MaxAttempts = maxAttempts,
        TimeoutMs = 60_000,
        DelayMinMs = 1,
        DelayMaxMs = 2,
    };

    private static HttpMessageInvoker CreateInvoker(HttpRetryHandler handler) =>
        new(handler);

    private static HttpRetryHandler CreateHandler(RetryOptions options, StubHandler inner)
    {
        var handler = new HttpRetryHandler(options, NullLogger<HttpRetryHandler>.Instance)
        {
            InnerHandler = inner,
        };
        return handler;
    }

    [Fact]
    public async Task SuccessResponse_IsNotRetried()
    {
        var inner = new StubHandler(HttpStatusCode.OK);
        using var invoker = CreateInvoker(CreateHandler(FastRetry(), inner));

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/");
        using var response = await invoker.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, inner.CallCount);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task TransientFailure_ThenSuccess_Retries(HttpStatusCode transient)
    {
        var inner = new StubHandler(transient, HttpStatusCode.OK);
        using var invoker = CreateInvoker(CreateHandler(FastRetry(), inner));

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/");
        using var response = await invoker.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.CallCount);
    }

    [Fact]
    public async Task PersistentFailure_ExhaustsAttempts_AndReturnsLastResponse()
    {
        // Always 503; with MaxAttempts == 3 the handler makes the initial call plus two retries.
        var inner = new StubHandler(Enumerable.Repeat(HttpStatusCode.ServiceUnavailable, 10).ToArray());
        using var invoker = CreateInvoker(CreateHandler(FastRetry(maxAttempts: 3), inner));

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/");
        using var response = await invoker.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(3, inner.CallCount);
    }

    [Fact]
    public async Task PostBody_IsResent_OnEachRetryAttempt()
    {
        var inner = new StubHandler(HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);
        using var invoker = CreateInvoker(CreateHandler(FastRetry(), inner));

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.test/")
        {
            Content = new StringContent("payload-body"),
        };
        using var response = await invoker.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.CallCount);
        Assert.Equal(new[] { "payload-body", "payload-body" }, inner.ReceivedBodies);
    }

    [Fact]
    public async Task RetryAfterHeader_IsHonoured_AndStillSucceeds()
    {
        var inner = new StubHandler(HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK)
        {
            RetryAfterSeconds = 0, // keep the test fast while exercising the Retry-After path
        };
        using var invoker = CreateInvoker(CreateHandler(FastRetry(), inner));

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/");
        using var response = await invoker.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.CallCount);
    }

    [Fact]
    public async Task Cancellation_DuringBackoff_Throws()
    {
        var inner = new StubHandler(Enumerable.Repeat(HttpStatusCode.ServiceUnavailable, 10).ToArray());
        var options = new RetryOptions
        {
            MaxAttempts = 5,
            TimeoutMs = 60_000,
            DelayMinMs = 10_000,
            DelayMaxMs = 10_000,
        };
        using var invoker = CreateInvoker(CreateHandler(options, inner));
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(50);

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => invoker.SendAsync(request, cts.Token));
    }

    /// <summary>Test double that returns a queued sequence of status codes and records each call.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Queue<HttpStatusCode> _statuses;

        public StubHandler(params HttpStatusCode[] statuses)
        {
            _statuses = new Queue<HttpStatusCode>(statuses);
        }

        public int CallCount { get; private set; }

        public List<string> ReceivedBodies { get; } = new();

        /// <summary>When set, a <c>Retry-After</c> header (in seconds) is attached to transient responses.</summary>
        public int? RetryAfterSeconds { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            if (request.Content is not null)
            {
                ReceivedBodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            }

            var status = _statuses.Count > 0 ? _statuses.Dequeue() : HttpStatusCode.OK;
            var response = new HttpResponseMessage(status);
            if (RetryAfterSeconds is { } seconds &&
                status is HttpStatusCode.ServiceUnavailable or HttpStatusCode.TooManyRequests)
            {
                response.Headers.RetryAfter =
                    new System.Net.Http.Headers.RetryConditionHeaderValue(System.TimeSpan.FromSeconds(seconds));
            }

            return response;
        }
    }
}
