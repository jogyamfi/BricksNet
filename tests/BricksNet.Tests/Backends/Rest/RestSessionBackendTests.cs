using System.Net;
using System.Text.Json;
using BricksNet.Backends.Rest.Dto;
using BricksNet.Connection;
using BricksNet.Contracts.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BricksNet.Backends.Rest;

public sealed class RestStatementPollerTests
{
    private const string RunningJson = """{"statement_id":"id-1","status":{"state":"RUNNING"}}""";
    private const string SucceededJson = """{"statement_id":"id-1","status":{"state":"SUCCEEDED"}}""";

    private static RestStatementPoller CreatePoller(RestTestHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://fake.local") };
        var bricks = new BricksNetHttpClient(httpClient, NullLogger<BricksNetHttpClient>.Instance);
        return new RestStatementPoller(new SqlExecHttpClient(bricks));
    }

    [Fact]
    public async Task PollUntilTerminalAsync_ReturnsImmediately_WhenTerminal()
    {
        var handler = new RestTestHandler((_, _) => (HttpStatusCode.OK, SucceededJson));
        var poller = CreatePoller(handler);

        var response = await poller.PollUntilTerminalAsync("id-1");

        Assert.Equal(StatementState.Succeeded, response.Status.State);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task PollUntilTerminalAsync_PollsUntilTerminal()
    {
        var call = 0;
        var handler = new RestTestHandler((_, _) =>
            (HttpStatusCode.OK, call++ == 0 ? RunningJson : SucceededJson));
        var poller = CreatePoller(handler);

        var response = await poller.PollUntilTerminalAsync("id-1");

        Assert.Equal(StatementState.Succeeded, response.Status.State);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task PollUntilTerminalAsync_Cancelled_Throws()
    {
        var handler = new RestTestHandler((_, _) => (HttpStatusCode.OK, RunningJson));
        var poller = CreatePoller(handler);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => poller.PollUntilTerminalAsync("id-1", cts.Token));
    }

    [Fact]
    public async Task PollUntilTerminalAsync_EmptyId_Throws()
    {
        var handler = new RestTestHandler((_, _) => (HttpStatusCode.OK, SucceededJson));
        var poller = CreatePoller(handler);

        await Assert.ThrowsAsync<ArgumentException>(() => poller.PollUntilTerminalAsync(string.Empty));
    }

    [Theory]
    [InlineData(nameof(StatementState.Succeeded), true)]
    [InlineData(nameof(StatementState.Failed), true)]
    [InlineData(nameof(StatementState.Canceled), true)]
    [InlineData(nameof(StatementState.Closed), true)]
    [InlineData(nameof(StatementState.Pending), false)]
    [InlineData(nameof(StatementState.Running), false)]
    public void IsTerminal_ClassifiesStates(string stateName, bool expected)
    {
        var state = Enum.Parse<StatementState>(stateName);
        Assert.Equal(expected, RestStatementPoller.IsTerminal(state));
    }
}

public sealed class RestSessionBackendTests
{
    private const string SucceededJson =
        """{"statement_id":"01ef-meta","status":{"state":"SUCCEEDED"}}""";

    private static (RestSessionBackend Backend, RestTestHandler Handler) CreateBackend(
        BackendSessionOptions? options = null)
    {
        var handler = new RestTestHandler((_, _) => (HttpStatusCode.OK, SucceededJson));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://fake.local") };
        var bricks = new BricksNetHttpClient(httpClient, NullLogger<BricksNetHttpClient>.Instance);
        var client = new SqlExecHttpClient(bricks);
        var poller = new RestStatementPoller(client);
        var backend = new RestSessionBackend(
            client, poller, "wh-1", options ?? new BackendSessionOptions());
        return (backend, handler);
    }

    private static string LastStatement(RestTestHandler handler)
    {
        var body = handler.Bodies[^1]!;
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("statement").GetString()!;
    }

    [Fact]
    public async Task GetCatalogsAsync_IssuesShowCatalogs()
    {
        var (backend, handler) = CreateBackend();

        await backend.GetCatalogsAsync();

        Assert.Equal("SHOW CATALOGS", LastStatement(handler));
    }

    [Fact]
    public async Task GetSchemasAsync_QuotesCatalog_AndLikePattern()
    {
        var (backend, handler) = CreateBackend();

        await backend.GetSchemasAsync("cat", "pat%");

        Assert.Equal("SHOW SCHEMAS IN `cat` LIKE 'pat%'", LastStatement(handler));
    }

    [Fact]
    public async Task GetSchemasAsync_EscapesBacktickInIdentifier()
    {
        var (backend, handler) = CreateBackend();

        await backend.GetSchemasAsync("ca`t", null);

        Assert.Equal("SHOW SCHEMAS IN `ca``t`", LastStatement(handler));
    }

    [Fact]
    public async Task GetTablesAsync_BuildsQualifiedName()
    {
        var (backend, handler) = CreateBackend();

        await backend.GetTablesAsync("cat", "sch", "t%");

        Assert.Equal("SHOW TABLES IN `cat`.`sch` LIKE 't%'", LastStatement(handler));
    }

    [Fact]
    public async Task GetColumnsAsync_BuildsThreePartName()
    {
        var (backend, handler) = CreateBackend();

        await backend.GetColumnsAsync("cat", "sch", "tbl", columnNamePattern: null);

        Assert.Equal("SHOW COLUMNS IN `cat`.`sch`.`tbl`", LastStatement(handler));
    }

    [Fact]
    public async Task GetColumnsAsync_WithoutTable_Throws()
    {
        var (backend, _) = CreateBackend();

        await Assert.ThrowsAsync<ArgumentException>(
            () => backend.GetColumnsAsync(null, null, null, null));
    }

    [Fact]
    public async Task ExecuteStatementAsync_CarriesSessionDefaults()
    {
        var (backend, handler) = CreateBackend(new BackendSessionOptions
        {
            Catalog = "main",
            Schema = "default",
            MaxRows = 500,
        });

        await backend.ExecuteStatementAsync("SELECT 1", options: null);

        var body = handler.Bodies[^1]!;
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        Assert.Equal("SELECT 1", root.GetProperty("statement").GetString());
        Assert.Equal("main", root.GetProperty("catalog").GetString());
        Assert.Equal("default", root.GetProperty("schema").GetString());
        // MaxRows is a client page size; sending it as row_limit would truncate results server-side.
        Assert.False(root.TryGetProperty("row_limit", out _));
    }

    [Fact]
    public async Task ExecuteStatementAsync_EmptySql_Throws()
    {
        var (backend, _) = CreateBackend();

        await Assert.ThrowsAsync<ArgumentException>(
            () => backend.ExecuteStatementAsync(string.Empty, options: null));
    }

    [Fact]
    public async Task CloseSessionAsync_IsNoOp()
    {
        var (backend, handler) = CreateBackend();

        await backend.CloseSessionAsync();

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void Id_IsAssigned()
    {
        var (backend, _) = CreateBackend();

        Assert.True(Guid.TryParse(backend.Id, out _));
    }
}
