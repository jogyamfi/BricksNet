using Xunit;

namespace BricksNet.IntegrationTests.FakeService;

/// <summary>
/// xUnit class fixture that owns the in-process <see cref="FakeHttpServer"/> for a test class. The
/// server is started once, the offline fixture set is materialised into a private temp directory,
/// and the active replayer is swapped per test via <see cref="UseFixtures"/>.
/// </summary>
public sealed class FakeServiceFixture : IAsyncLifetime
{
    private readonly FakeHttpServer _server = new();

    /// <summary>Root directory holding the materialised fixture set.</summary>
    public string FixtureRoot { get; private set; } = string.Empty;

    /// <summary>The base URL the fake server is listening on.</summary>
    public string BaseUrl => _server.BaseUrl;

    public async Task InitializeAsync()
    {
        await _server.StartAsync().ConfigureAwait(false);
        FixtureRoot = Path.Combine(Path.GetTempPath(), "bricksnet-fixtures", Guid.NewGuid().ToString("N"));
        await FixtureLibrary.SeedAsync(FixtureRoot, _server.BaseUrl).ConfigureAwait(false);
    }

    public async Task DisposeAsync()
    {
        await _server.DisposeAsync().ConfigureAwait(false);
        try
        {
            if (Directory.Exists(FixtureRoot))
            {
                Directory.Delete(FixtureRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup of the temp fixture directory.
        }
    }

    /// <summary>Loads the fixtures for one test (<c>{root}/{serviceType}/{testName}</c>) as the active responder.</summary>
    internal FakeServiceReplayer UseFixtures(string serviceType, string testName)
    {
        var replayer = FakeServiceReplayer.Load(Path.Combine(FixtureRoot, serviceType, testName));
        _server.Responder = replayer;
        return replayer;
    }
}
