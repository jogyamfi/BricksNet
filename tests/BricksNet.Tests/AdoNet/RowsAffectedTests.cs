using System.Data;
using BricksNet.Results;
using Xunit;

namespace BricksNet.AdoNet;

/// <summary>
/// Databricks reports the rows changed by DML as a <c>num_affected_rows</c> summary result set;
/// these tests cover surfacing it through <c>ExecuteNonQuery</c> and <c>RecordsAffected</c>.
/// </summary>
public sealed class RowsAffectedTests
{
    private static readonly IReadOnlyList<ColumnInfo> DmlSchema =
    [
        new("num_affected_rows", "BIGINT", 0, IsNullable: true, Precision: null, Scale: null),
        new("num_inserted_rows", "BIGINT", 1, IsNullable: true, Precision: null, Scale: null),
    ];

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> DmlRows(long affected) =>
        FakeFluent.Rows(FakeFluent.Row(("num_affected_rows", affected), ("num_inserted_rows", affected)));

    private static BricksNetConnection OpenConnection(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        IReadOnlyList<ColumnInfo> schema)
    {
        var connection = new BricksNetConnection(
            "Host=h;HttpPath=/sql/1.0/warehouses/x;Token=tok",
            (_, _) => new FakeFluent.FakeClient(rows));
        connection.Open();
        ((FakeFluent.FakeSession)connection.GetOpenSession()).Schema = schema;
        return connection;
    }

    [Fact]
    public async Task ExecuteNonQuery_DmlSummary_ReturnsAffectedRows()
    {
        using var connection = OpenConnection(DmlRows(3), DmlSchema);
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE t SET x = 1";

        Assert.Equal(3, await command.ExecuteNonQueryAsync());
        Assert.True(((FakeFluent.FakeSession)connection.GetOpenSession()).LastOperation!.Closed);
    }

    [Fact]
    public void ExecuteNonQuery_Sync_ReturnsAffectedRows()
    {
        using var connection = OpenConnection(DmlRows(2), DmlSchema);
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM t";

        Assert.Equal(2, command.ExecuteNonQuery());
    }

    [Fact]
    public async Task ExecuteNonQuery_CountAboveInt32_Saturates()
    {
        using var connection = OpenConnection(DmlRows(5_000_000_000L), DmlSchema);
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM big";

        Assert.Equal(int.MaxValue, await command.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task ExecuteNonQuery_NoResultSchema_ReturnsMinusOne_WithoutFetching()
    {
        // DDL reports no schema; its rows must not be read as a count.
        using var connection = OpenConnection(DmlRows(9), []);
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE t (id INT)";

        Assert.Equal(-1, await command.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task ExecuteNonQuery_QueryResult_ReturnsMinusOne()
    {
        IReadOnlyList<ColumnInfo> schema =
        [
            new("num_affected_rows", "BIGINT", 0, IsNullable: true, Precision: null, Scale: null),
            new("name", "STRING", 1, IsNullable: true, Precision: null, Scale: null),
        ];
        using var connection = OpenConnection(
            FakeFluent.Rows(FakeFluent.Row(("num_affected_rows", 4L), ("name", "x"))),
            schema);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT num_affected_rows, name FROM audit";

        Assert.Equal(-1, await command.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task Reader_DmlSummary_ReportsRecordsAffected_AndStillExposesRow()
    {
        using var connection = OpenConnection(DmlRows(7), DmlSchema);
        using var command = connection.CreateCommand();
        command.CommandText = "MERGE INTO t USING s ON t.id = s.id WHEN MATCHED THEN DELETE";

        await using var reader = await command.ExecuteReaderAsync();

        Assert.Equal(7, reader.RecordsAffected);
        Assert.True(await reader.ReadAsync());
        Assert.Equal(7L, reader.GetInt64(0));
    }

    [Fact]
    public async Task Reader_Query_ReportsMinusOne()
    {
        using var connection = OpenConnection(FakeFluent.Rows(FakeFluent.Row(("id", 1))), []);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id FROM t";

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.Default);

        Assert.Equal(-1, reader.RecordsAffected);
    }

    [Theory]
    [InlineData(new[] { "num_affected_rows" }, true)]
    [InlineData(new[] { "NUM_AFFECTED_ROWS", "num_updated_rows", "num_deleted_rows", "num_inserted_rows" }, true)]
    [InlineData(new[] { "num_inserted_rows" }, false)]
    [InlineData(new[] { "num_affected_rows", "id" }, false)]
    [InlineData(new string[0], false)]
    public void IsSummary_RequiresOnlyNumColumns_IncludingAffectedRows(string[] columns, bool expected)
    {
        Assert.Equal(expected, DmlSummary.IsSummary(columns));
    }

    [Fact]
    public void ReadAffectedRows_ParsesStringsAndRejectsNull()
    {
        Assert.Equal(12, DmlSummary.ReadAffectedRows(FakeFluent.Row(("num_affected_rows", "12"))));
        Assert.Equal(-1, DmlSummary.ReadAffectedRows(FakeFluent.Row(("num_affected_rows", null))));
        Assert.Equal(-1, DmlSummary.ReadAffectedRows(FakeFluent.Row(("other", 1))));
    }
}
