using System.Collections.Generic;
using System.Threading.Tasks;
using BricksNet.IntegrationTests.FakeService;
using Xunit;

namespace BricksNet.IntegrationTests;

/// <summary>
/// Replay integration tests for the REST (SQL Statement Execution API) backend, driven through the
/// full fluent client against the in-process fake service.
/// </summary>
public sealed class RestBackendIntegrationTests : IntegrationTestBase, IDisposable
{
    public RestBackendIntegrationTests(FakeServiceFixture fixture)
        : base(fixture)
    {
    }

    public void Dispose() => DisposeClients();

    [Fact]
    public async Task ExecuteSelect_PollsToTerminal_ThenReturnsArrowRows()
    {
        var replayer = LoadFixtures("rest", "execute_select");

        await using var client = CreateClient();
        await client.ConnectAsync();
        await using var session = await client.OpenSessionAsync();
        await using var operation = await session.ExecuteStatementAsync("SELECT * FROM sample");

        var rows = await operation.FetchAllAsync<IReadOnlyDictionary<string, object?>>();

        Assert.Equal(3, rows.Count);
        Assert.Equal(1, Convert.ToInt32(rows[0]["id"]));
        Assert.Equal("alpha", rows[0]["name"]);
        Assert.Equal(true, rows[0]["active"]);
        Assert.Null(rows[2]["name"]);

        // The POST returned RUNNING, so exactly one status GET must have been replayed.
        Assert.Equal(1, replayer.HitsFor("POST", "/api/2.0/sql/statements"));
        Assert.Equal(1, replayer.HitsFor("GET", $"/api/2.0/sql/statements/{FixtureLibrary.SelectStatementId}"));
    }

    [Fact]
    public async Task ExecuteDirectResults_ReturnsRows_WithoutAStatusPoll()
    {
        var replayer = LoadFixtures("rest", "execute_direct_results");

        await using var client = CreateClient();
        await using var session = await client.OpenSessionAsync();
        await using var operation = await session.ExecuteStatementAsync("SELECT * FROM sample");

        var rows = await operation.FetchAllAsync<IReadOnlyDictionary<string, object?>>();

        Assert.Equal(3, rows.Count);
        Assert.Equal(new[] { 1, 2, 3 }, rows.Select(r => Convert.ToInt32(r["id"])).ToArray());

        // The POST carried direct results, so no separate status GET is issued.
        Assert.Equal(1, replayer.HitsFor("POST", "/api/2.0/sql/statements"));
        Assert.Equal(0, replayer.HitsFor("GET", $"/api/2.0/sql/statements/{FixtureLibrary.DirectStatementId}"));
    }

    [Fact]
    public async Task FetchChunk_ReturnsBoundedNumberOfRows()
    {
        LoadFixtures("rest", "execute_direct_results");

        await using var client = CreateClient();
        await using var session = await client.OpenSessionAsync();
        await using var operation = await session.ExecuteStatementAsync("SELECT * FROM sample");

        var firstChunk = await operation.FetchChunkAsync<IReadOnlyDictionary<string, object?>>(2);

        Assert.Equal(2, firstChunk.Count);
        Assert.Equal(1, Convert.ToInt32(firstChunk[0]["id"]));
        Assert.Equal(2, Convert.ToInt32(firstChunk[1]["id"]));
    }
}
