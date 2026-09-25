using System.Net;
using BricksNet.AdoNet;
using BricksNet.Backends.Rest.Dto;
using BricksNet.Connection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BricksNet.Backends.Rest;

public sealed class RestOperationBackendTests
{
    private const string Manifest =
        "\"manifest\":{\"format\":\"JSON_ARRAY\",\"total_chunk_count\":3,"
        + "\"schema\":{\"columns\":[{\"name\":\"id\",\"type_name\":\"INT\",\"position\":0}]}}";

    private static string Succeeded(string statementId, string result) =>
        "{\"statement_id\":\"" + statementId + "\",\"status\":{\"state\":\"SUCCEEDED\"},"
        + Manifest + ",\"result\":" + result + "}";

    private static (BricksNetOperation Operation, RestTestHandler Handler) CreateOperation(
        string initialResponse,
        Func<string, string>? chunkResponder = null)
    {
        var handler = new RestTestHandler((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            return path.Contains("/result/chunks/", StringComparison.Ordinal)
                ? (HttpStatusCode.OK, chunkResponder!(path[(path.LastIndexOf('/') + 1)..]))
                : (HttpStatusCode.OK, initialResponse);
        });

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://fake.local") };
        var bricks = new BricksNetHttpClient(httpClient, NullLogger<BricksNetHttpClient>.Instance);
        var client = new SqlExecHttpClient(bricks);
        var poller = new RestStatementPoller(client);

        var initial = System.Text.Json.JsonSerializer.Deserialize(
            initialResponse, SqlExecJsonContext.Default.ExecuteStatementResponse)!;
        var backend = new RestOperationBackend(client, poller, initial);
        var operation = new BricksNetOperation(backend, 10_000, "rest", NullLoggerFactory.Instance);
        return (operation, handler);
    }

    [Fact]
    public async Task FetchAllAsync_FollowsNextChunkIndex_AcrossAllChunks()
    {
        var initial = Succeeded(
            "st-1", """{"chunk_index":0,"next_chunk_index":1,"data_array":[["1"],["2"]]}""");
        var (operation, handler) = CreateOperation(initial, index => index switch
        {
            "1" => """{"chunk_index":1,"next_chunk_index":2,"data_array":[["3"]]}""",
            "2" => """{"chunk_index":2,"data_array":[["4"],["5"]]}""",
            _ => throw new InvalidOperationException($"Unexpected chunk {index}"),
        });

        var rows = await operation.FetchAllAsync<IReadOnlyDictionary<string, object?>>();

        Assert.Equal(new object?[] { 1, 2, 3, 4, 5 }, rows.Select(r => r["id"]));
        Assert.Equal(
            new[] { "/api/2.0/sql/statements/st-1/result/chunks/1", "/api/2.0/sql/statements/st-1/result/chunks/2" },
            handler.Requests.Select(r => r.RequestUri!.AbsolutePath));
    }

    [Fact]
    public async Task FetchAllAsync_SingleChunk_MakesNoChunkRequests()
    {
        var initial = Succeeded("st-2", """{"chunk_index":0,"data_array":[["7"]]}""");
        var (operation, handler) = CreateOperation(initial);

        var rows = await operation.FetchAllAsync<IReadOnlyDictionary<string, object?>>();

        Assert.Single(rows);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task FetchAllAsync_FailedStatement_SurfacesServerErrorAndSqlState()
    {
        const string initial = """
            {"statement_id":"st-3","status":{"state":"FAILED","error":{"error_code":"BAD_REQUEST",
             "message":"[TABLE_OR_VIEW_NOT_FOUND] The table `missing` cannot be found. SQLSTATE: 42P01"}}}
            """;
        var (operation, _) = CreateOperation(initial);

        var ex = await Assert.ThrowsAsync<BricksNetException>(
            () => operation.FetchAllAsync<IReadOnlyDictionary<string, object?>>());

        Assert.Contains("TABLE_OR_VIEW_NOT_FOUND", ex.Message, StringComparison.Ordinal);
        Assert.StartsWith("[BAD_REQUEST]", ex.Message, StringComparison.Ordinal);
        Assert.Equal("42P01", ex.SqlState);
        Assert.Equal("st-3", ex.QueryId);
    }

    [Fact]
    public async Task QueryTimeout_Exceeded_CancelsStatement_AndThrowsTimeout()
    {
        const string running = """{"statement_id":"st-5","status":{"state":"RUNNING"}}""";
        var handler = new RestTestHandler((_, _) => (HttpStatusCode.OK, running));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://fake.local") };
        var client = new SqlExecHttpClient(
            new BricksNetHttpClient(httpClient, NullLogger<BricksNetHttpClient>.Instance));
        var initial = System.Text.Json.JsonSerializer.Deserialize(
            running, SqlExecJsonContext.Default.ExecuteStatementResponse)!;
        var backend = new RestOperationBackend(
            client, new RestStatementPoller(client), initial, queryTimeout: TimeSpan.Zero);
        var operation = new BricksNetOperation(backend, 10_000, "rest", NullLoggerFactory.Instance);

        var ex = await Assert.ThrowsAsync<BricksNetException>(() => operation.WaitForCompletionAsync());

        Assert.Equal("HYT00", ex.SqlState);
        Assert.Contains(handler.Requests, r =>
            r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.EndsWith("/st-5/cancel", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WaitForCompletionAsync_FailedStatement_SurfacesServerError()
    {
        const string initial = """
            {"statement_id":"st-4","status":{"state":"FAILED","error":{"message":"Division by zero"}}}
            """;
        var (operation, _) = CreateOperation(initial);

        var ex = await Assert.ThrowsAsync<BricksNetException>(() => operation.WaitForCompletionAsync());

        Assert.Equal("Division by zero", ex.Message);
    }
}
