using System.Text.Json;
using System.Text.Json.Nodes;

namespace BricksNet.IntegrationTests.FakeService;

/// <summary>
/// Materialises the offline fixture set used by the replay integration tests. Fixtures are written
/// under <c>{root}/{service_type}/{test_name}/step-{n}.json</c> using deterministic Arrow payloads,
/// which keeps the suite self-contained (no live workspace or opaque checked-in binary required).
/// In a real workflow these files would instead be captured by <see cref="FakeServiceRecorder"/>.
/// </summary>
internal static class FixtureLibrary
{
    private static readonly JsonSerializerOptions WriteOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private static readonly IReadOnlyList<ArrowFixtures.SampleRow> SampleRows = new[]
    {
        new ArrowFixtures.SampleRow(1, "alpha", true),
        new ArrowFixtures.SampleRow(2, "beta", false),
        new ArrowFixtures.SampleRow(3, null, true),
    };

    public const string SelectStatementId = "01ef-fixture-select";
    public const string DirectStatementId = "01ef-fixture-direct";
    public const string AdoStatementId = "01ef-fixture-ado";
    public const string AdoDmlStatementId = "01ef-fixture-ado-dml";
    public const string CloudFetchStatementId = "01ef-fixture-cloudfetch";

    private const string StatementsPath = "/api/2.0/sql/statements";

    /// <summary>Writes the full fixture set rooted at <paramref name="root"/>.</summary>
    /// <param name="root">Directory under which service-type folders are created.</param>
    /// <param name="baseUrl">The fake server base URL, baked into CloudFetch external links.</param>
    public static async Task SeedAsync(string root, string baseUrl)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);
        ArgumentException.ThrowIfNullOrEmpty(baseUrl);

        await SeedExecuteSelectAsync(root).ConfigureAwait(false);
        await SeedExecuteDirectAsync(root).ConfigureAwait(false);
        await SeedAdoNetAsync(root).ConfigureAwait(false);
        await SeedAdoNetDmlAsync(root).ConfigureAwait(false);
        await SeedCloudFetchAsync(root, baseUrl).ConfigureAwait(false);
    }

    // ---- rest/execute_select: POST returns RUNNING, GET returns the terminal inline result ----
    private static async Task SeedExecuteSelectAsync(string root)
    {
        var dir = Path.Combine(root, "rest", "execute_select");

        await WriteAsync(dir, 0, "POST", StatementsPath, JsonResponse(RunningBody(SelectStatementId)))
            .ConfigureAwait(false);
        await WriteAsync(
            dir, 1, "GET", $"{StatementsPath}/{SelectStatementId}",
            JsonResponse(SampleSuccessBody(SelectStatementId))).ConfigureAwait(false);
    }

    // ---- rest/execute_direct_results: POST returns the terminal result inline ----
    private static async Task SeedExecuteDirectAsync(string root)
    {
        var dir = Path.Combine(root, "rest", "execute_direct_results");

        await WriteAsync(
            dir, 0, "POST", StatementsPath,
            JsonResponse(SampleSuccessBody(DirectStatementId))).ConfigureAwait(false);
    }

    // ---- adonet: identical shape, distinct statement id for isolation ----
    private static async Task SeedAdoNetAsync(string root)
    {
        var dir = Path.Combine(root, "adonet", "execute_reader");

        await WriteAsync(
            dir, 0, "POST", StatementsPath,
            JsonResponse(SampleSuccessBody(AdoStatementId))).ConfigureAwait(false);
    }

    // ---- adonet/execute_dml: Databricks reports DML row counts as a num_* summary result set ----
    private static async Task SeedAdoNetDmlAsync(string root)
    {
        var dir = Path.Combine(root, "adonet", "execute_dml");

        await WriteAsync(
            dir, 0, "POST", StatementsPath,
            JsonResponse(DmlSummaryBody(AdoDmlStatementId, affectedRows: 2))).ConfigureAwait(false);
    }

    // ---- cloudfetch: POST returns external links; two ordered chunk files are served separately ----
    private static async Task SeedCloudFetchAsync(string root, string baseUrl)
    {
        var dir = Path.Combine(root, "cloudfetch", "execute_select");
        var trimmedBase = baseUrl.TrimEnd('/');

        var chunk0 = await ArrowFixtures.BuildIdColumnAsync(new[] { 1, 2 }).ConfigureAwait(false);
        var chunk1 = await ArrowFixtures.BuildIdColumnAsync(new[] { 3, 4 }).ConfigureAwait(false);

        await WriteAsync(
            dir, 0, "POST", StatementsPath,
            JsonResponse(CloudFetchBody(CloudFetchStatementId, trimmedBase, chunk0.Length, chunk1.Length)))
            .ConfigureAwait(false);

        // External chunk payloads — returned out of declaration order to prove the handler re-orders.
        await WriteAsync(dir, 1, "GET", "/external/cf-1", BinaryResponse(chunk1)).ConfigureAwait(false);
        await WriteAsync(dir, 2, "GET", "/external/cf-0", BinaryResponse(chunk0)).ConfigureAwait(false);
    }

    // ---- response body builders ----

    private static JsonObject RunningBody(string statementId) => new()
    {
        ["statement_id"] = statementId,
        ["status"] = new JsonObject { ["state"] = "RUNNING" },
    };

    // INLINE disposition only supports JSON_ARRAY: every cell is a JSON string (or null).
    private static JsonObject SampleSuccessBody(string statementId) => new()
    {
        ["statement_id"] = statementId,
        ["status"] = new JsonObject { ["state"] = "SUCCEEDED" },
        ["manifest"] = new JsonObject
        {
            ["format"] = "JSON_ARRAY",
            ["schema"] = new JsonObject
            {
                ["column_count"] = 3,
                ["columns"] = new JsonArray
                {
                    Column("id", "INT", 0),
                    Column("name", "STRING", 1),
                    Column("active", "BOOLEAN", 2),
                },
            },
            ["total_row_count"] = SampleRows.Count,
            ["total_chunk_count"] = 1,
        },
        ["result"] = new JsonObject
        {
            ["chunk_index"] = 0,
            ["row_count"] = SampleRows.Count,
            ["data_array"] = SampleDataArray(),
        },
    };

    private static JsonObject DmlSummaryBody(string statementId, int affectedRows)
    {
        var count = affectedRows.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return new JsonObject
        {
            ["statement_id"] = statementId,
            ["status"] = new JsonObject { ["state"] = "SUCCEEDED" },
            ["manifest"] = new JsonObject
            {
                ["format"] = "JSON_ARRAY",
                ["schema"] = new JsonObject
                {
                    ["column_count"] = 2,
                    ["columns"] = new JsonArray
                    {
                        Column("num_affected_rows", "BIGINT", 0),
                        Column("num_inserted_rows", "BIGINT", 1),
                    },
                },
                ["total_row_count"] = 1,
                ["total_chunk_count"] = 1,
            },
            ["result"] = new JsonObject
            {
                ["chunk_index"] = 0,
                ["row_count"] = 1,
                ["data_array"] = new JsonArray { new JsonArray(JsonValue.Create(count), JsonValue.Create(count)) },
            },
        };
    }

    private static JsonArray SampleDataArray()
    {
        var rows = new JsonArray();
        foreach (var row in SampleRows)
        {
            rows.Add(new JsonArray(
                JsonValue.Create(row.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                row.Name is null ? null : JsonValue.Create(row.Name),
                JsonValue.Create(row.Active ? "true" : "false")));
        }

        return rows;
    }

    private static JsonObject CloudFetchBody(string statementId, string baseUrl, int size0, int size1) => new()
    {
        ["statement_id"] = statementId,
        ["status"] = new JsonObject { ["state"] = "SUCCEEDED" },
        ["manifest"] = new JsonObject
        {
            ["format"] = "ARROW_STREAM",
            ["schema"] = new JsonObject
            {
                ["column_count"] = 1,
                ["columns"] = new JsonArray { Column("id", "INT", 0) },
            },
            ["total_row_count"] = 4,
            ["total_chunk_count"] = 2,
        },
        ["result"] = new JsonObject
        {
            ["chunk_index"] = 0,
            ["external_links"] = new JsonArray
            {
                ExternalLink($"{baseUrl}/external/cf-0", 0, 0, 2, size0),
                ExternalLink($"{baseUrl}/external/cf-1", 1, 2, 2, size1),
            },
        },
    };

    private static JsonObject Column(string name, string typeName, int position) => new()
    {
        ["name"] = name,
        ["type_name"] = typeName,
        ["position"] = position,
    };

    private static JsonObject ExternalLink(string url, int chunkIndex, int rowOffset, int rowCount, int byteCount) => new()
    {
        ["external_link"] = url,
        ["expiration"] = "2999-01-01T00:00:00Z",
        ["chunk_index"] = chunkIndex,
        ["row_offset"] = rowOffset,
        ["row_count"] = rowCount,
        ["byte_count"] = byteCount,
    };

    // ---- io ----

    private static StoredResponse JsonResponse(JsonNode body) => new()
    {
        Status = 200,
        ContentType = "application/json",
        Json = body,
    };

    private static StoredResponse BinaryResponse(byte[] payload) => new()
    {
        Status = 200,
        ContentType = "application/octet-stream",
        Base64 = Convert.ToBase64String(payload),
    };

    private static async Task WriteAsync(string dir, int step, string method, string path, StoredResponse response)
    {
        Directory.CreateDirectory(dir);
        var exchange = new StoredExchange
        {
            Request = new StoredRequest { Method = method, Path = path },
            Response = response,
        };

        var file = Path.Combine(dir, $"step-{step}.json");
        await File.WriteAllTextAsync(file, JsonSerializer.Serialize(exchange, WriteOptions)).ConfigureAwait(false);
    }
}
