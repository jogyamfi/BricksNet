using BricksNet.Connection;
using BricksNet.Contracts.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BricksNet.IntegrationTests.FakeService;

/// <summary>
/// Base class for replay integration tests. Provides the shared <see cref="FakeServiceFixture"/>,
/// per-test fixture loading, and a factory that builds a <see cref="BricksNetClient"/> wired to the
/// in-process fake server.
/// </summary>
public abstract class IntegrationTestBase : IClassFixture<FakeServiceFixture>
{
    private readonly List<HttpClient> _httpClients = new();

    protected IntegrationTestBase(FakeServiceFixture fixture)
    {
        Fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));
    }

    /// <summary>The shared fake-service fixture.</summary>
    protected FakeServiceFixture Fixture { get; }

    /// <summary>Loads the fixtures for a single test as the fake server's active responder.</summary>
    internal FakeServiceReplayer LoadFixtures(string serviceType, string testName) =>
        Fixture.UseFixtures(serviceType, testName);

    /// <summary>
    /// Builds a fluent client pointed at the fake server. Only <see cref="FakeServiceMode.Replay"/>
    /// is supported offline; the record/passthrough modes require live credentials.
    /// </summary>
    protected BricksNetClient CreateClient(FakeServiceMode mode = FakeServiceMode.Replay)
    {
        if (mode != FakeServiceMode.Replay)
        {
            throw new NotSupportedException(
                "Only Replay mode is available in the offline suite; Record/Passthrough require a live workspace.");
        }

        var inner = new HttpClient { BaseAddress = new Uri(Fixture.BaseUrl) };
        _httpClients.Add(inner);
        var bricksHttp = new BricksNetHttpClient(inner, NullLogger<BricksNetHttpClient>.Instance);

        return new BricksNetClient(BackendOptions(), bricksHttp);
    }

    /// <summary>The REST client options used by integration tests (host/path are placeholders).</summary>
    protected static BricksNetClientOptions BackendOptions() => new()
    {
        Host = "fake.local",
        HttpPath = "/sql/1.0/warehouses/fake",
        UseThrift = false,
    };

    /// <summary>Disposes any HttpClients created by <see cref="CreateClient"/>.</summary>
    protected void DisposeClients()
    {
        foreach (var client in _httpClients)
        {
            client.Dispose();
        }

        _httpClients.Clear();
    }
}
