using System.Net;
using BricksNet.AdoNet;
using BricksNet.Backends.Rest.Dto;
using BricksNet.Connection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BricksNet.Backends.Rest;

public sealed class SqlExecHttpClientTests
{
    private const string SucceededJson =
        """{"statement_id":"01ef-abc","status":{"state":"SUCCEEDED"}}""";

    private static SqlExecHttpClient CreateClient(RestTestHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://fake.local") };
        var bricks = new BricksNetHttpClient(httpClient, NullLogger<BricksNetHttpClient>.Instance);
        return new SqlExecHttpClient(bricks);
    }

    [Fact]
    public async Task ExecuteAsync_PostsToStatementsPath_AndParsesResponse()
    {
        var handler = new RestTestHandler((_, _) => (HttpStatusCode.OK, SucceededJson));
        var client = CreateClient(handler);

        var response = await client.ExecuteAsync(new ExecuteStatementRequest
        {
            WarehouseId = "wh-1",
            Statement = "SELECT 1",
        });

        Assert.Equal("01ef-abc", response.StatementId);
        Assert.Equal(StatementState.Succeeded, response.Status.State);
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.EndsWith("/api/2.0/sql/statements", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Contains("SELECT 1", handler.Bodies[0]);
    }

    [Fact]
    public async Task GetStatementAsync_EscapesId_AndParsesResponse()
    {
        var handler = new RestTestHandler((_, _) => (HttpStatusCode.OK, SucceededJson));
        var client = CreateClient(handler);

        var response = await client.GetStatementAsync("01ef abc/d");

        Assert.Equal(StatementState.Succeeded, response.Status.State);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Contains("01ef%20abc%2Fd", handler.Requests[0].RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task GetChunkAsync_ReturnsResultData()
    {
        const string chunkJson = """{"chunk_index":2,"row_count":1}""";
        var handler = new RestTestHandler((_, _) => (HttpStatusCode.OK, chunkJson));
        var client = CreateClient(handler);

        var chunk = await client.GetChunkAsync("id-1", 2);

        Assert.NotNull(chunk);
        Assert.Contains("/result/chunks/2", handler.Requests[0].RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task CancelAsync_PostsToCancelEndpoint()
    {
        var handler = new RestTestHandler((_, _) => (HttpStatusCode.OK, string.Empty));
        var client = CreateClient(handler);

        await client.CancelAsync("id-1");

        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Equal("/api/2.0/sql/statements/id-1/cancel", handler.Requests[0].RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task ExecuteAsync_NonSuccess_WithBody_ThrowsWithStatusCode()
    {
        var handler = new RestTestHandler((_, _) => (HttpStatusCode.BadRequest, "bad statement"));
        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<BricksNetException>(
            () => client.ExecuteAsync(new ExecuteStatementRequest { WarehouseId = "w", Statement = "x" }));

        Assert.Equal(400, ex.ErrorCode);
        Assert.Contains("bad statement", ex.Message);
    }

    [Fact]
    public async Task GetStatementAsync_NonSuccess_WithoutBody_ThrowsReasonPhrase()
    {
        var handler = new RestTestHandler((_, _) => (HttpStatusCode.InternalServerError, string.Empty));
        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<BricksNetException>(() => client.GetStatementAsync("id-1"));

        Assert.Equal(500, ex.ErrorCode);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyBody_Throws()
    {
        var handler = new RestTestHandler((_, _) => (HttpStatusCode.OK, "null"));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<BricksNetException>(
            () => client.ExecuteAsync(new ExecuteStatementRequest { WarehouseId = "w", Statement = "x" }));
    }

    [Fact]
    public async Task CancelAsync_NonSuccess_Throws()
    {
        var handler = new RestTestHandler((_, _) => (HttpStatusCode.NotFound, "missing"));
        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<BricksNetException>(() => client.CancelAsync("id-1"));

        Assert.Equal(404, ex.ErrorCode);
        Assert.Equal("id-1", ex.QueryId);
    }
}
