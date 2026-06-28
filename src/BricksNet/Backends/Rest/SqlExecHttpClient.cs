using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BricksNet.AdoNet;
using BricksNet.Backends.Rest.Dto;
using BricksNet.Connection;

namespace BricksNet.Backends.Rest;

/// <summary>
/// Thin wrapper over <see cref="BricksNetHttpClient"/> that speaks the Databricks SQL
/// Statement Execution API. It owns the <c>/api/2.0/sql/statements</c> path prefix and the
/// shared, source-generated JSON (de)serialisation. Cross-cutting concerns (auth, retry,
/// proxy, TLS) are handled by the underlying handler pipeline.
/// </summary>
internal sealed class SqlExecHttpClient
{
    private const string StatementsPath = "/api/2.0/sql/statements";

    private static readonly MediaTypeHeaderValue JsonMediaType = new("application/json")
    {
        CharSet = "utf-8",
    };

    private readonly BricksNetHttpClient _httpClient;

    public SqlExecHttpClient(BricksNetHttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    /// <summary>Submits a statement for execution (<c>POST /api/2.0/sql/statements</c>).</summary>
    public async Task<ExecuteStatementResponse> ExecuteAsync(
        ExecuteStatementRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var json = JsonSerializer.Serialize(request, SqlExecJsonContext.Default.ExecuteStatementRequest);
        using var content = new StringContent(json, Encoding.UTF8);
        content.Headers.ContentType = JsonMediaType;

        using var response = await _httpClient
            .PostAsync(StatementsPath, content, cancellationToken)
            .ConfigureAwait(false);

        return await ReadResponseAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Polls a statement's status (<c>GET /api/2.0/sql/statements/{id}</c>).</summary>
    public async Task<ExecuteStatementResponse> GetStatementAsync(
        string statementId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(statementId);

        using var response = await _httpClient
            .GetAsync($"{StatementsPath}/{Uri.EscapeDataString(statementId)}", cancellationToken)
            .ConfigureAwait(false);

        return await ReadResponseAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Fetches a result chunk by index
    /// (<c>GET /api/2.0/sql/statements/{id}/result/chunks/{index}</c>).
    /// </summary>
    public async Task<ResultData> GetChunkAsync(
        string statementId,
        long chunkIndex,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(statementId);

        var uri = $"{StatementsPath}/{Uri.EscapeDataString(statementId)}/result/chunks/{chunkIndex}";
        using var response = await _httpClient.GetAsync(uri, cancellationToken).ConfigureAwait(false);

        await EnsureSuccessAsync(response, statementId, cancellationToken).ConfigureAwait(false);

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            var chunk = await JsonSerializer
                .DeserializeAsync(stream, SqlExecJsonContext.Default.ResultData, cancellationToken)
                .ConfigureAwait(false);
            return chunk ?? throw new BricksNetException(
                "The SQL Statement Execution API returned an empty result chunk.");
        }
    }

    /// <summary>Cancels a running statement (<c>DELETE /api/2.0/sql/statements/{id}</c>).</summary>
    public async Task CancelAsync(string statementId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(statementId);

        using var response = await _httpClient
            .DeleteAsync($"{StatementsPath}/{Uri.EscapeDataString(statementId)}", cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, statementId, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ExecuteStatementResponse> ReadResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(response, statementId: null, cancellationToken).ConfigureAwait(false);

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            var result = await JsonSerializer
                .DeserializeAsync(stream, SqlExecJsonContext.Default.ExecuteStatementResponse, cancellationToken)
                .ConfigureAwait(false);
            return result ?? throw new BricksNetException(
                "The SQL Statement Execution API returned an empty response body.");
        }
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        string? statementId,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var message = string.IsNullOrWhiteSpace(body)
            ? $"The SQL Statement Execution API returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase})."
            : $"The SQL Statement Execution API returned HTTP {(int)response.StatusCode}: {body}";

        throw new BricksNetException(message, sqlState: null, errorCode: (int)response.StatusCode, queryId: statementId);
    }
}
