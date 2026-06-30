using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BricksNet.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BricksNet.Connection.Handlers;

public sealed class AuthHeaderHandlerTests
{
    [Fact]
    public async Task SendAsync_AttachesBearerToken_FromAuthProvider()
    {
        var inner = new CapturingHandler();
        var handler = new AuthHeaderHandler(new StubAuthProvider("token-abc"), NullLogger<AuthHeaderHandler>.Instance)
        {
            InnerHandler = inner,
        };
        using var invoker = new HttpMessageInvoker(handler);

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/");
        using var response = await invoker.SendAsync(request, CancellationToken.None);

        Assert.NotNull(inner.LastAuthorization);
        Assert.Equal("Bearer", inner.LastAuthorization!.Scheme);
        Assert.Equal("token-abc", inner.LastAuthorization.Parameter);
    }

    [Fact]
    public async Task SendAsync_RefreshesToken_OnEachCall()
    {
        var provider = new StubAuthProvider("first", "second");
        var inner = new CapturingHandler();
        var handler = new AuthHeaderHandler(provider, NullLogger<AuthHeaderHandler>.Instance)
        {
            InnerHandler = inner,
        };
        using var invoker = new HttpMessageInvoker(handler);

        using (var request1 = new HttpRequestMessage(HttpMethod.Get, "https://example.test/"))
        {
            using var _ = await invoker.SendAsync(request1, CancellationToken.None);
            Assert.Equal("first", inner.LastAuthorization!.Parameter);
        }

        using (var request2 = new HttpRequestMessage(HttpMethod.Get, "https://example.test/"))
        {
            using var _ = await invoker.SendAsync(request2, CancellationToken.None);
            Assert.Equal("second", inner.LastAuthorization!.Parameter);
        }
    }

    [Fact]
    public void Constructor_NullAuthProvider_Throws()
    {
        Assert.Throws<System.ArgumentNullException>(() =>
            new AuthHeaderHandler(null!, NullLogger<AuthHeaderHandler>.Instance));
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public System.Net.Http.Headers.AuthenticationHeaderValue? LastAuthorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastAuthorization = request.Headers.Authorization;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class StubAuthProvider : IAuthProvider
    {
        private readonly Queue<string> _tokens;
        private string _last;

        public StubAuthProvider(params string[] tokens)
        {
            _tokens = new Queue<string>(tokens);
            _last = tokens.Length > 0 ? tokens[^1] : string.Empty;
        }

        public Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
        {
            if (_tokens.Count > 0)
            {
                _last = _tokens.Dequeue();
            }

            return Task.FromResult(_last);
        }
    }
}
