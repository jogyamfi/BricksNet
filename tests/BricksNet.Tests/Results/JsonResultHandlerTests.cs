using System.Text;
using Xunit;
using BricksNet.Results;

namespace BricksNet.Results;

public class JsonResultHandlerTests
{
    private static ColumnInfo Col(int ordinal, string name, string typeName) =>
        new ColumnInfo(name, typeName, ordinal, IsNullable: true, Precision: null, Scale: null);

    private static ReadOnlyMemory<byte> Utf8(string json) =>
        Encoding.UTF8.GetBytes(json);

    // ---- empty / null payload ----

    [Fact]
    public void EmptyPayload_HasMoreRowsFalse()
    {
        var handler = new JsonResultHandler(ReadOnlyMemory<byte>.Empty, Array.Empty<ColumnInfo>());
        Assert.False(handler.HasMoreRows);
    }

    [Fact]
    public async Task EmptyPayload_FetchNextChunk_ReturnsEmpty()
    {
        var handler = new JsonResultHandler(ReadOnlyMemory<byte>.Empty, Array.Empty<ColumnInfo>());
        var rows = await handler.FetchNextChunkAsync();
        Assert.Empty(rows);
    }

    // ---- basic int + string parsing ----

    [Fact]
    public async Task ParsesRowArrayIntoRowDictionaries()
    {
        var json = Utf8("[[1, \"Alice\"],[2, \"Bob\"]]");
        var schema = new[] { Col(0, "id", "INT"), Col(1, "name", "STRING") };
        var handler = new JsonResultHandler(json, schema);

        Assert.True(handler.HasMoreRows);
        var rows = await handler.FetchNextChunkAsync();

        Assert.Equal(2, rows.Count);
        Assert.Equal(1, rows[0]["id"]);
        Assert.Equal("Alice", rows[0]["name"]);
        Assert.Equal(2, rows[1]["id"]);
        Assert.Equal("Bob", rows[1]["name"]);
    }

    // ---- null cells ----

    [Fact]
    public async Task NullCell_ReturnsNull()
    {
        var json = Utf8("[[null, \"test\"]]");
        var schema = new[] { Col(0, "x", "INT"), Col(1, "y", "STRING") };
        var handler = new JsonResultHandler(json, schema);

        var rows = await handler.FetchNextChunkAsync();
        Assert.Single(rows);
        Assert.Null(rows[0]["x"]);
        Assert.Equal("test", rows[0]["y"]);
    }

    // ---- type coercions ----

    [Theory]
    [InlineData("[[true]]", "BOOLEAN", typeof(bool), true)]
    [InlineData("[[\"42\"]]", "TINYINT", typeof(sbyte), (sbyte)42)]
    [InlineData("[[\"1000\"]]", "SMALLINT", typeof(short), (short)1000)]
    [InlineData("[[\"2000000\"]]", "INT", typeof(int), 2_000_000)]
    [InlineData("[[\"9999999999\"]]", "BIGINT", typeof(long), 9_999_999_999L)]
    [InlineData("[[\"3.14\"]]", "FLOAT", typeof(float), 3.14f)]
    [InlineData("[[\"2.718\"]]", "DOUBLE", typeof(double), 2.718d)]
    public async Task TypeCoercion_Primitives(string json, string typeName, Type expectedType, object expectedValue)
    {
        var schema = new[] { Col(0, "v", typeName) };
        var handler = new JsonResultHandler(Utf8(json), schema);
        var rows = await handler.FetchNextChunkAsync();

        Assert.Single(rows);
        var value = rows[0]["v"];
        Assert.NotNull(value);
        Assert.IsType(expectedType, value);
        Assert.Equal(expectedValue, value);
    }

    [Fact]
    public async Task TypeCoercion_Decimal()
    {
        var json = Utf8("[[\"123.45\"]]");
        var schema = new[] { Col(0, "d", "DECIMAL(10,2)") }; // with parens
        var handler = new JsonResultHandler(json, schema);
        var rows = await handler.FetchNextChunkAsync();

        Assert.Single(rows);
        Assert.Equal(123.45m, Assert.IsType<decimal>(rows[0]["d"]));
    }

    [Fact]
    public async Task TypeCoercion_Date()
    {
        var json = Utf8("[[\"2024-03-15\"]]");
        var schema = new[] { Col(0, "d", "DATE") };
        var handler = new JsonResultHandler(json, schema);
        var rows = await handler.FetchNextChunkAsync();

        Assert.Single(rows);
        Assert.Equal(new DateOnly(2024, 3, 15), Assert.IsType<DateOnly>(rows[0]["d"]));
    }

    [Fact]
    public async Task TypeCoercion_Binary_FromBase64()
    {
        var expected = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var base64 = Convert.ToBase64String(expected);
        var json = Utf8($"[[\"{base64}\"]]");
        var schema = new[] { Col(0, "b", "BINARY") };
        var handler = new JsonResultHandler(json, schema);
        var rows = await handler.FetchNextChunkAsync();

        Assert.Single(rows);
        Assert.Equal(expected, Assert.IsType<byte[]>(rows[0]["b"]));
    }

    [Fact]
    public async Task UnknownTypeName_ReturnsRawString()
    {
        var json = Utf8("[[\"raw-value\"]]");
        var schema = new[] { Col(0, "v", "CUSTOM_TYPE") };
        var handler = new JsonResultHandler(json, schema);
        var rows = await handler.FetchNextChunkAsync();

        Assert.Equal("raw-value", rows[0]["v"]);
    }

    // ---- single-shot: second call returns empty ----

    [Fact]
    public async Task SecondFetchNextChunk_ReturnsEmpty()
    {
        var json = Utf8("[[1]]");
        var schema = new[] { Col(0, "n", "INT") };
        var handler = new JsonResultHandler(json, schema);

        await handler.FetchNextChunkAsync();
        Assert.False(handler.HasMoreRows);

        var second = await handler.FetchNextChunkAsync();
        Assert.Empty(second);
    }
}
