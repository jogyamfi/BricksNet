using BricksNet.AdoNet;
using BricksNet.Contracts.Options;
using BricksNetParameter = BricksNet.Contracts.Options.BricksNetParameter;
using Xunit;

namespace BricksNet.IntegrationTests.Live;

/// <summary>
/// Fluent API tests against a real SQL warehouse and the fixtures from
/// <c>scripts/databricks/01_setup.sql</c>. Each test runs over REST (<c>false</c>) and Thrift
/// (<c>true</c>); a SQL warehouse HTTP path accepts both protocols.
/// </summary>
[Trait("Category", "E2E")]
public sealed class LiveFluentTests
{
    private const long BigTableRows = 2_000_000;
    private const long BigTableIdSum = BigTableRows * (BigTableRows - 1) / 2;

    [LiveTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelectPeople_ReturnsKnownRowsAndNulls(bool useThrift)
    {
        await using var client = LiveWorkspace.CreateClient(useThrift);
        await using var session = await client.OpenSessionAsync();
        await using var operation = await session.ExecuteStatementAsync(
            $"SELECT id, name, city, active, score FROM {LiveWorkspace.Table("people")} ORDER BY id");

        var rows = await operation.FetchAllAsync<IReadOnlyDictionary<string, object?>>();

        Assert.Equal(5, rows.Count);
        Assert.Equal(1, Convert.ToInt32(rows[0]["id"]));
        Assert.Equal("Ada", rows[0]["name"]);
        Assert.Equal(true, rows[0]["active"]);
        Assert.Equal(91.5, Convert.ToDouble(rows[0]["score"]));
        Assert.Null(rows[3]["score"]);
        Assert.Null(rows[4]["name"]);
        Assert.Null(rows[4]["city"]);
    }

    [LiveTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SessionCatalogAndSchema_ResolveUnqualifiedNames(bool useThrift)
    {
        await using var client = LiveWorkspace.CreateClient(useThrift);
        await using var session = await client.OpenSessionAsync(
            new SessionOptions { Catalog = LiveWorkspace.Catalog, Schema = LiveWorkspace.Schema });
        await using var operation = await session.ExecuteStatementAsync(
            "SELECT current_catalog() AS c, current_schema() AS s, (SELECT count(*) FROM active_people) AS n");

        var row = Assert.Single(await operation.FetchAllAsync<IReadOnlyDictionary<string, object?>>());

        Assert.Equal(LiveWorkspace.Catalog, row["c"]);
        Assert.Equal(LiveWorkspace.Schema, row["s"]);
        Assert.Equal(3, Convert.ToInt32(row["n"]));
    }

    [LiveTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AllTypes_TypicalValues_Convert(bool useThrift)
    {
        var row = await AllTypesRowAsync(useThrift, id: 1);

        Assert.Equal(true, row["c_boolean"]);
        Assert.Equal(7, Convert.ToInt32(row["c_tinyint"]));
        Assert.Equal(300, Convert.ToInt32(row["c_smallint"]));
        Assert.Equal(70000, Convert.ToInt32(row["c_int"]));
        Assert.Equal(9_000_000_000L, Convert.ToInt64(row["c_bigint"]));
        Assert.Equal(1.5f, Convert.ToSingle(row["c_float"]));
        Assert.Equal(2.25, Convert.ToDouble(row["c_double"]));
        Assert.Equal(12345.6789m, Convert.ToDecimal(row["c_decimal"]));
        Assert.Equal("12345678901234567890123456789012345678", Convert.ToString(row["c_decimal_max"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("hello, world", row["c_string"]);
        Assert.Equal(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, Assert.IsType<byte[]>(row["c_binary"]));
        Assert.Equal(new DateOnly(2024, 2, 29), row["c_date"]);
        Assert.NotNull(row["c_timestamp"]);
        Assert.NotNull(row["c_timestamp_ntz"]);
        Assert.NotNull(row["c_array"]);
        Assert.NotNull(row["c_map"]);
        Assert.NotNull(row["c_struct"]);
    }

    [LiveTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AllTypes_EdgeValues_Convert(bool useThrift)
    {
        var row = await AllTypesRowAsync(useThrift, id: 2);

        Assert.Equal(sbyte.MinValue, Convert.ToSByte(row["c_tinyint"]));
        Assert.Equal(short.MinValue, Convert.ToInt16(row["c_smallint"]));
        Assert.Equal(int.MinValue, Convert.ToInt32(row["c_int"]));
        Assert.Equal(long.MinValue, Convert.ToInt64(row["c_bigint"]));
        Assert.Equal(double.MaxValue, Convert.ToDouble(row["c_double"]));
        Assert.Equal(-99999999999999.9999m, Convert.ToDecimal(row["c_decimal"]));
        Assert.Equal("unicode: é ü 中文 🚀 and 'quotes'", row["c_string"]);
        Assert.Empty(Assert.IsType<byte[]>(row["c_binary"]));
        Assert.Equal(new DateOnly(1, 1, 1), row["c_date"]);
    }

    [LiveTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AllTypes_NullRow_ReturnsNullForEveryColumn(bool useThrift)
    {
        var row = await AllTypesRowAsync(useThrift, id: 3);

        foreach (var (column, value) in row)
        {
            if (column != "id")
            {
                Assert.True(value is null, $"Column {column} should be NULL but was {value}.");
            }
        }
    }

    [LiveTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NamedParameters_AreBound(bool useThrift)
    {
        await using var client = LiveWorkspace.CreateClient(useThrift);
        await using var session = await client.OpenSessionAsync();
        await using var operation = await session.ExecuteStatementAsync(
            $"SELECT id, name FROM {LiveWorkspace.Table("people")} WHERE active = :active AND name LIKE :pattern ORDER BY id",
            new StatementOptions
            {
                Parameters = [new BricksNetParameter("active", true), new BricksNetParameter("pattern", "%a%")],
            });

        var rows = await operation.FetchAllAsync<IReadOnlyDictionary<string, object?>>();

        // Chiamaka matches the pattern but is inactive; Dmitri is active but has no lowercase 'a'.
        Assert.Equal(new[] { 1, 2 }, rows.Select(r => Convert.ToInt32(r["id"])).ToArray());
    }

    [LiveTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ParameterValue_WithQuotes_IsNotInjected(bool useThrift)
    {
        await using var client = LiveWorkspace.CreateClient(useThrift);
        await using var session = await client.OpenSessionAsync();
        await using var operation = await session.ExecuteStatementAsync(
            $"SELECT count(*) AS n FROM {LiveWorkspace.Table("people")} WHERE name = :name",
            new StatementOptions { Parameters = [new BricksNetParameter("name", "x' OR '1'='1")] });

        var row = Assert.Single(await operation.FetchAllAsync<IReadOnlyDictionary<string, object?>>());

        Assert.Equal(0, Convert.ToInt32(row["n"]));
    }

    [LiveTheory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task LargeResult_AllRowsArrive(bool useThrift, bool useCloudFetch)
    {
        await using var client = LiveWorkspace.CreateClient(useThrift, useCloudFetch);
        await using var session = await client.OpenSessionAsync();
        await using var operation = await session.ExecuteStatementAsync(
            $"SELECT id FROM {LiveWorkspace.Table("big_table")}");

        long count = 0;
        long sum = 0;
        IReadOnlyList<IReadOnlyDictionary<string, object?>> chunk;
        while ((chunk = await operation.FetchChunkAsync<IReadOnlyDictionary<string, object?>>(100_000)).Count > 0)
        {
            foreach (var row in chunk)
            {
                count++;
                sum += Convert.ToInt64(row["id"]);
            }
        }

        Assert.Equal(BigTableRows, count);
        Assert.Equal(BigTableIdSum, sum);
    }

    [LiveTheory]
    [InlineData(false)]
    public async Task InlineJsonResult_UnderLimit_AllRowsArrive(bool useThrift)
    {
        await using var client = LiveWorkspace.CreateClient(useThrift, useCloudFetch: false);
        await using var session = await client.OpenSessionAsync();
        await using var operation = await session.ExecuteStatementAsync(
            $"SELECT id, label, day FROM {LiveWorkspace.Table("big_table")} WHERE id < 100000");

        var rows = await operation.FetchAllAsync<IReadOnlyDictionary<string, object?>>();

        Assert.Equal(100_000, rows.Count);
        Assert.Equal(new DateOnly(2020, 1, 1), rows.First(r => Convert.ToInt64(r["id"]) == 0)["day"]);
    }

    [LiveTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetSchema_DescribesColumns(bool useThrift)
    {
        await using var client = LiveWorkspace.CreateClient(useThrift);
        await using var session = await client.OpenSessionAsync();
        await using var operation = await session.ExecuteStatementAsync(
            $"SELECT id, name, created_at FROM {LiveWorkspace.Table("people")} LIMIT 1");

        var columns = await operation.GetSchemaAsync();

        Assert.Equal(new[] { "id", "name", "created_at" }, columns.Select(c => c.Name).ToArray());
    }

    [LiveTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Metadata_FindsFixtureSchemaTablesAndColumns(bool useThrift)
    {
        await using var client = LiveWorkspace.CreateClient(useThrift);
        await using var session = await client.OpenSessionAsync();

        Assert.True(await ContainsValueAsync(session.GetCatalogsAsync(), LiveWorkspace.Catalog), "catalog not listed");
        Assert.True(await ContainsValueAsync(session.GetSchemasAsync(LiveWorkspace.Catalog), LiveWorkspace.Schema), "schema not listed");
        Assert.True(await ContainsValueAsync(session.GetTablesAsync(LiveWorkspace.Catalog, LiveWorkspace.Schema), "people"), "table not listed");
        Assert.True(await ContainsValueAsync(session.GetColumnsAsync(LiveWorkspace.Catalog, LiveWorkspace.Schema, "people"), "created_at"), "column not listed");
    }

    [LiveTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingTable_ThrowsWithErrorDetails(bool useThrift)
    {
        await using var client = LiveWorkspace.CreateClient(useThrift);
        await using var session = await client.OpenSessionAsync();

        var ex = await Assert.ThrowsAsync<BricksNetException>(async () =>
        {
            await using var operation = await session.ExecuteStatementAsync(
                $"SELECT * FROM {LiveWorkspace.Table("does_not_exist")}");
            await operation.FetchAllAsync<IReadOnlyDictionary<string, object?>>();
        });

        Assert.Contains("TABLE_OR_VIEW_NOT_FOUND", ex.Message + ex.ErrorClass, StringComparison.Ordinal);
        Assert.False(ex.IsTransient);
    }

    [LiveTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SyntaxError_Throws(bool useThrift)
    {
        await using var client = LiveWorkspace.CreateClient(useThrift);
        await using var session = await client.OpenSessionAsync();

        await Assert.ThrowsAsync<BricksNetException>(async () =>
        {
            await using var operation = await session.ExecuteStatementAsync("SELEC 1");
            await operation.FetchAllAsync<IReadOnlyDictionary<string, object?>>();
        });
    }

    private static async Task<IReadOnlyDictionary<string, object?>> AllTypesRowAsync(bool useThrift, int id)
    {
        await using var client = LiveWorkspace.CreateClient(useThrift);
        await using var session = await client.OpenSessionAsync();
        await using var operation = await session.ExecuteStatementAsync(
            $"SELECT * FROM {LiveWorkspace.Table("all_types")} WHERE id = {id}");

        return Assert.Single(await operation.FetchAllAsync<IReadOnlyDictionary<string, object?>>());
    }

    private static async Task<bool> ContainsValueAsync(Task<Contracts.IBricksNetOperation> metadataCall, string expected)
    {
        await using var operation = await metadataCall;
        var rows = await operation.FetchAllAsync<IReadOnlyDictionary<string, object?>>();

        // REST (SHOW ...) and Thrift (JDBC-style) name the metadata columns differently.
        return rows.Any(row => row.Values.Any(v => string.Equals(v as string, expected, StringComparison.OrdinalIgnoreCase)));
    }
}
