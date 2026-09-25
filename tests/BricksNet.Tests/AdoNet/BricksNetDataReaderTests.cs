using System.Data;
using System.Data.Common;
using BricksNet.Contracts;
using BricksNet.Results;
using Xunit;

namespace BricksNet.AdoNet;

/// <summary>
/// Focused tests for the typed accessor surface of <see cref="BricksNetDataReader"/>.
/// The reader is constructed directly over an in-memory operation so every
/// <c>Get*</c> conversion path can be exercised deterministically.
/// </summary>
public sealed class BricksNetDataReaderTests
{
    private static readonly DateTime SampleDate = new(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);
    private static readonly DateTimeOffset SampleOffset = new(SampleDate, TimeSpan.Zero);
    private static readonly DateOnly SampleDateOnly = new(2024, 1, 2);
    private static readonly Guid SampleGuid = Guid.Parse("11112222-3333-4444-5555-666677778888");
    private static readonly byte[] SampleBytes = [1, 2, 3, 4];

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> TypedRows() =>
        FakeFluent.Rows(
            FakeFluent.Row(
                ("i32", 42),
                ("i64", 9_000_000_000L),
                ("i16", (short)7),
                ("u8", (byte)5),
                ("flag", true),
                ("f32", 1.5f),
                ("f64", 2.5d),
                ("dec", 3.25m),
                ("str", "hello"),
                ("dt", SampleDate),
                ("dto", SampleOffset),
                ("dateonly", SampleDateOnly),
                ("guid", SampleGuid),
                ("guidstr", SampleGuid.ToString()),
                ("bin", SampleBytes),
                ("ch", 'Z')));

    private static BricksNetDataReader CreateReader(
        IReadOnlyList<IReadOnlyDictionary<string, object?>>? rows = null,
        CommandBehavior behavior = CommandBehavior.Default,
        DbConnection? connectionToClose = null)
    {
        rows ??= TypedRows();
        var operation = new FakeFluent.FakeOperation(rows);
        var first = operation
            .FetchChunkAsync<IReadOnlyDictionary<string, object?>>(10_000)
            .GetAwaiter()
            .GetResult();
        return new BricksNetDataReader(operation, [], first, 10_000, behavior, connectionToClose);
    }

    [Fact]
    public void TypedAccessors_ConvertEachColumn()
    {
        using var reader = CreateReader();
        Assert.True(reader.Read());

        Assert.Equal(42, reader.GetInt32(reader.GetOrdinal("i32")));
        Assert.Equal(9_000_000_000L, reader.GetInt64(reader.GetOrdinal("i64")));
        Assert.Equal((short)7, reader.GetInt16(reader.GetOrdinal("i16")));
        Assert.Equal((byte)5, reader.GetByte(reader.GetOrdinal("u8")));
        Assert.True(reader.GetBoolean(reader.GetOrdinal("flag")));
        Assert.Equal(1.5f, reader.GetFloat(reader.GetOrdinal("f32")));
        Assert.Equal(2.5d, reader.GetDouble(reader.GetOrdinal("f64")));
        Assert.Equal(3.25m, reader.GetDecimal(reader.GetOrdinal("dec")));
        Assert.Equal("hello", reader.GetString(reader.GetOrdinal("str")));
        Assert.Equal('Z', reader.GetChar(reader.GetOrdinal("ch")));
    }

    [Fact]
    public void GetDateTime_HandlesDateTimeOffsetAndDateOnly()
    {
        using var reader = CreateReader();
        Assert.True(reader.Read());

        Assert.Equal(SampleDate, reader.GetDateTime(reader.GetOrdinal("dt")));
        Assert.Equal(SampleDate, reader.GetDateTime(reader.GetOrdinal("dto")));
        Assert.Equal(SampleDateOnly.ToDateTime(TimeOnly.MinValue), reader.GetDateTime(reader.GetOrdinal("dateonly")));
    }

    [Fact]
    public void GetGuid_AcceptsGuidAndString()
    {
        using var reader = CreateReader();
        Assert.True(reader.Read());

        Assert.Equal(SampleGuid, reader.GetGuid(reader.GetOrdinal("guid")));
        Assert.Equal(SampleGuid, reader.GetGuid(reader.GetOrdinal("guidstr")));
    }

    [Fact]
    public void GetGuid_Throws_ForIncompatibleType()
    {
        using var reader = CreateReader();
        Assert.True(reader.Read());

        Assert.Throws<InvalidCastException>(() => reader.GetGuid(reader.GetOrdinal("i32")));
    }

    [Fact]
    public void GetBytes_CopiesIntoBuffer_AndReportsLength()
    {
        using var reader = CreateReader();
        Assert.True(reader.Read());
        var ordinal = reader.GetOrdinal("bin");

        Assert.Equal(SampleBytes.Length, reader.GetBytes(ordinal, 0, null, 0, 0));

        var buffer = new byte[2];
        var copied = reader.GetBytes(ordinal, 1, buffer, 0, 2);
        Assert.Equal(2, copied);
        Assert.Equal(new byte[] { 2, 3 }, buffer);
    }

    [Fact]
    public void GetChars_CopiesIntoBuffer_AndReportsLength()
    {
        using var reader = CreateReader();
        Assert.True(reader.Read());
        var ordinal = reader.GetOrdinal("str");

        Assert.Equal("hello".Length, reader.GetChars(ordinal, 0, null, 0, 0));

        var buffer = new char[3];
        var copied = reader.GetChars(ordinal, 1, buffer, 0, 3);
        Assert.Equal(3, copied);
        Assert.Equal(new[] { 'e', 'l', 'l' }, buffer);
    }

    [Fact]
    public void GetFieldValue_ReturnsTypedAndConvertedValues()
    {
        using var reader = CreateReader();
        Assert.True(reader.Read());

        Assert.Equal(42, reader.GetFieldValue<int>(reader.GetOrdinal("i32")));
        Assert.Equal(42L, reader.GetFieldValue<long>(reader.GetOrdinal("i32")));
        Assert.Equal("hello", reader.GetFieldValue<string>(reader.GetOrdinal("str")));
    }

    [Fact]
    public void GetFieldValue_ReturnsNull_ForReferenceTypeOnNull()
    {
        var rows = FakeFluent.Rows(FakeFluent.Row(("str", (object?)null)));
        using var reader = CreateReader(rows);
        Assert.True(reader.Read());

        Assert.Null(reader.GetFieldValue<string?>(0));
    }

    [Fact]
    public void GetFieldValue_Throws_ForValueTypeOnNull()
    {
        var rows = FakeFluent.Rows(FakeFluent.Row(("n", (object?)null)));
        using var reader = CreateReader(rows);
        Assert.True(reader.Read());

        Assert.Throws<InvalidCastException>(() => reader.GetFieldValue<int>(0));
    }

    [Fact]
    public void Indexers_ReturnValuesByOrdinalAndName()
    {
        using var reader = CreateReader();
        Assert.True(reader.Read());

        Assert.Equal(42, reader[reader.GetOrdinal("i32")]);
        Assert.Equal("hello", reader["str"]);
    }

    [Fact]
    public void GetValues_FillsArray_UpToFieldCount()
    {
        using var reader = CreateReader();
        Assert.True(reader.Read());

        var values = new object[reader.FieldCount];
        var written = reader.GetValues(values);

        Assert.Equal(reader.FieldCount, written);
        Assert.Equal(42, values[reader.GetOrdinal("i32")]);
    }

    [Fact]
    public void GetDataTypeName_MapsClrTypesToSqlNames()
    {
        using var reader = CreateReader();

        Assert.Equal("INT", reader.GetDataTypeName(reader.GetOrdinal("i32")));
        Assert.Equal("BIGINT", reader.GetDataTypeName(reader.GetOrdinal("i64")));
        Assert.Equal("SMALLINT", reader.GetDataTypeName(reader.GetOrdinal("i16")));
        Assert.Equal("TINYINT", reader.GetDataTypeName(reader.GetOrdinal("u8")));
        Assert.Equal("BOOLEAN", reader.GetDataTypeName(reader.GetOrdinal("flag")));
        Assert.Equal("FLOAT", reader.GetDataTypeName(reader.GetOrdinal("f32")));
        Assert.Equal("DOUBLE", reader.GetDataTypeName(reader.GetOrdinal("f64")));
        Assert.Equal("DECIMAL", reader.GetDataTypeName(reader.GetOrdinal("dec")));
        Assert.Equal("STRING", reader.GetDataTypeName(reader.GetOrdinal("str")));
        Assert.Equal("TIMESTAMP", reader.GetDataTypeName(reader.GetOrdinal("dt")));
        Assert.Equal("TIMESTAMP", reader.GetDataTypeName(reader.GetOrdinal("dto")));
        Assert.Equal("DATE", reader.GetDataTypeName(reader.GetOrdinal("dateonly")));
        Assert.Equal("BINARY", reader.GetDataTypeName(reader.GetOrdinal("bin")));
    }

    [Fact]
    public void Metadata_ReportsReaderShape()
    {
        using var reader = CreateReader();

        Assert.Equal(0, reader.Depth);
        Assert.Equal(-1, reader.RecordsAffected);
        Assert.True(reader.HasRows);
        Assert.False(reader.IsClosed);
        Assert.False(reader.NextResult());
    }

    [Fact]
    public async Task NextResultAsync_And_IsDBNullAsync_Behaviors()
    {
        await using var reader = CreateReader();
        Assert.False(await reader.NextResultAsync(CancellationToken.None));

        Assert.True(await reader.ReadAsync());
        Assert.False(await reader.IsDBNullAsync(reader.GetOrdinal("i32"), CancellationToken.None));
    }

    [Fact]
    public void HasRows_IsFalse_ForEmptyResult()
    {
        var reader = CreateReader(FakeFluent.Rows());

        Assert.False(reader.HasRows);
        Assert.Equal(0, reader.FieldCount);
        Assert.False(reader.Read());
    }

    [Fact]
    public void GetOrdinal_Throws_WhenColumnMissing()
    {
        using var reader = CreateReader();

        Assert.Throws<IndexOutOfRangeException>(() => reader.GetOrdinal("nope"));
    }

    [Fact]
    public void Accessor_Throws_BeforeRead()
    {
        using var reader = CreateReader();

        Assert.Throws<InvalidOperationException>(() => reader.GetValue(0));
    }

    [Fact]
    public void Accessor_Throws_AfterClose()
    {
        var reader = CreateReader();
        reader.Read();
        reader.Close();

        Assert.True(reader.IsClosed);
        Assert.Throws<InvalidOperationException>(() => reader.GetValue(0));
    }

    [Fact]
    public void GetValue_Throws_ForOrdinalOutOfRange()
    {
        using var reader = CreateReader();
        reader.Read();

        Assert.Throws<IndexOutOfRangeException>(() => reader.GetValue(999));
    }

    [Fact]
    public async Task CloseAsync_ClosesUnderlyingOperation_AndConnection()
    {
        var rows = TypedRows();
        var operation = new FakeFluent.FakeOperation(rows);
        var first = await operation.FetchChunkAsync<IReadOnlyDictionary<string, object?>>(10_000);
        var connection = new BricksNetConnection();
        using var reader = new BricksNetDataReader(
            operation, [], first, 10_000, CommandBehavior.CloseConnection, connection);

        await reader.CloseAsync();

        Assert.True(operation.Closed);
        Assert.True(reader.IsClosed);
    }

    // ---- server-reported schema ----

    private static ColumnInfo Col(int ordinal, string name, string type, int? precision = null, int? scale = null) =>
        new(name, type, ordinal, IsNullable: true, precision, scale);

    [Fact]
    public void Schema_EmptyResult_StillExposesColumns()
    {
        var operation = new FakeFluent.FakeOperation([]);
        var schema = new[] { Col(0, "id", "INT"), Col(1, "amount", "DECIMAL", 10, 2) };
        using var reader = new BricksNetDataReader(operation, schema, [], 10_000, CommandBehavior.Default, null);

        Assert.False(reader.HasRows);
        Assert.Equal(2, reader.FieldCount);
        Assert.Equal("amount", reader.GetName(1));
        Assert.Equal(typeof(decimal), reader.GetFieldType(1));
        Assert.Equal("DECIMAL", reader.GetDataTypeName(1));

        var table = reader.GetSchemaTable();
        Assert.Equal((short)10, table.Rows[1][SchemaTableColumn.NumericPrecision]);
        Assert.Equal((short)2, table.Rows[1][SchemaTableColumn.NumericScale]);
    }

    [Fact]
    public void Schema_AllNullColumn_UsesDeclaredType()
    {
        var rows = FakeFluent.Rows(FakeFluent.Row(("id", 1), ("ts", null)));
        var operation = new FakeFluent.FakeOperation(rows);
        var schema = new[] { Col(0, "id", "INT"), Col(1, "ts", "TIMESTAMP") };
        using var reader = new BricksNetDataReader(operation, schema, rows, 10_000, CommandBehavior.Default, null);

        Assert.Equal(typeof(DateTimeOffset), reader.GetFieldType(1));
        Assert.Equal(typeof(int), reader.GetFieldType(0));
    }

    [Fact]
    public void Schema_DuplicateNames_MatchSuffixedRowKeys()
    {
        var rows = FakeFluent.Rows(FakeFluent.Row(("id", 1), ("id_1", 2)));
        var operation = new FakeFluent.FakeOperation(rows);
        var schema = new[] { Col(0, "id", "INT"), Col(1, "id", "INT") };
        using var reader = new BricksNetDataReader(operation, schema, rows, 10_000, CommandBehavior.Default, null);

        Assert.True(reader.Read());
        Assert.Equal(2, reader.FieldCount);
        Assert.Equal(1, reader.GetInt32(0));
        Assert.Equal(2, reader.GetInt32(1));
    }
}
