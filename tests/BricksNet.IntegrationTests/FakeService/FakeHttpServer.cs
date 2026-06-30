using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BricksNet.IntegrationTests.FakeService;

/// <summary>
/// An in-process HTTP server (Kestrel bound to a random loopback port) that fronts the active
/// <see cref="IFakeResponder"/>. A real socket is used — rather than an in-memory test handler — so
/// the CloudFetch download path (which uses an independent <see cref="HttpClient"/>) can reach the
/// fake external-link endpoints.
/// </summary>
internal sealed class FakeHttpServer : IAsyncDisposable
{
    private WebApplication? _app;

    /// <summary>The responder used to handle incoming requests. Set per test.</summary>
    public IFakeResponder? Responder { get; set; }

    /// <summary>The base URL the server is listening on (for example <c>http://127.0.0.1:54123</c>).</summary>
    public string BaseUrl { get; private set; } = string.Empty;

    public async Task StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        var app = builder.Build();
        app.Run(HandleAsync);

        await app.StartAsync().ConfigureAwait(false);

        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()
            ?? throw new InvalidOperationException("The fake server exposed no address feature.");
        BaseUrl = addresses.Addresses.First();
        _app = app;
    }

    private async Task HandleAsync(HttpContext context)
    {
        var responder = Responder;
        if (responder is not null && await responder.TryRespondAsync(context).ConfigureAwait(false))
        {
            return;
        }

        context.Response.StatusCode = StatusCodes.Status404NotFound;
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync().ConfigureAwait(false);
            _app = null;
        }
    }
}
