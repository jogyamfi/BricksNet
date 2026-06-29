using System.Data;
using System.Data.Common;
using BricksNet.Contracts;
using BricksNet.Contracts.Options;
using Xunit;

namespace BricksNet.AdoNet;

[Collection("AdoNetClientFactory")]
public sealed class AdoNetIntegrationTests : IDisposable
{
    private static readonly IReadOnlyList<IReadOnlyDictionary<string, object?>> SampleRows =
        FakeFluent.Rows(
            FakeFluent.Row(("id", 1), ("name", "alpha"), ("active", true)),
            FakeFluent.Row(("id", 2), ("name", "beta"), ("active", false)),
            FakeFluent.Row(("id", 3), ("name", null), ("active", true)));

    public AdoNetIntegrationTests()
    {
        BricksNetConnection.ClientFactory =
            (_, _) => new FakeFluent.FakeClient(SampleRows);
    }

    public void Dispose() => BricksNetConnection.ClientFactory = null;

    private static BricksNetConnection NewConnection() =>
        new("Host=h;HttpPath=/sql/1.0/warehouses/x;Token=tok;Catalog=main;Schema=default");

    [Fact]
    public async Task OpenAsync_TransitionsState_ToOpen()
    {
        await using var connection = NewConnection();
        var states = new List<ConnectionState>();
        connection.StateChange += (_, e) => states.Add(e.CurrentState);

        await connection.OpenAsync();

        Assert.Equal(ConnectionState.Open, connection.State);
        Assert.Contains(ConnectionState.Connecting, states);
        Assert.Contains(ConnectionState.Open, states);
    }

    [Fact]
    public async Task OpenAsync_Throws_WhenNoFactoryConfigured()
    {
        BricksNetConnection.ClientFactory = null;
        await using var connection = NewConnection();

        await Assert.ThrowsAsync<InvalidOperationException>(() => connection.OpenAsync());
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    [Fact]
    public async Task CloseAsync_TransitionsState_ToClosed()
    {
        await using var connection = NewConnection();
        await connection.OpenAsync();

        await connection.CloseAsync();

        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    [Fact]
    public async Task ExecuteReader_ReadsAllRows_AndColumns()
    {
        await using var connection = NewConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM t";

        await using var reader = await command.ExecuteReaderAsync();

        Assert.Equal(3, reader.FieldCount);
        Assert.Equal("id", reader.GetName(0));
        Assert.Equal("name", reader.GetName(1));
        Assert.Equal(typeof(int), reader.GetFieldType(0));

        var ids = new List<int>();
        while (await reader.ReadAsync())
        {
            ids.Add(reader.GetInt32(reader.GetOrdinal("id")));
        }

        Assert.Equal(new[] { 1, 2, 3 }, ids);
    }

    [Fact]
    public async Task ExecuteReader_SurfacesNulls_AsDbNull()
    {
        await using var connection = NewConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM t";

        await using var reader = await command.ExecuteReaderAsync();
        var nameOrdinal = reader.GetOrdinal("name");

        Assert.True(await reader.ReadAsync());
        Assert.False(reader.IsDBNull(nameOrdinal));
        Assert.Equal("alpha", reader.GetString(nameOrdinal));

        Assert.True(await reader.ReadAsync());
        Assert.True(await reader.ReadAsync());
        Assert.True(reader.IsDBNull(nameOrdinal));
        Assert.Equal(DBNull.Value, reader.GetValue(nameOrdinal));
    }

    [Fact]
    public async Task ExecuteScalar_ReturnsFirstColumnOfFirstRow()
    {
        await using var connection = NewConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id FROM t";

        var result = await command.ExecuteScalarAsync();

        Assert.Equal(1, result);
    }

    [Fact]
    public async Task ExecuteNonQuery_ReturnsMinusOne()
    {
        await using var connection = NewConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO t VALUES (1)";

        var result = await command.ExecuteNonQueryAsync();

        Assert.Equal(-1, result);
    }

    [Fact]
    public async Task GetSchemaTable_ReturnsColumnMetadata()
    {
        await using var connection = NewConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM t";

        await using var reader = await command.ExecuteReaderAsync();
        var schema = reader.GetSchemaTable();

        Assert.NotNull(schema);
        Assert.Equal(3, schema!.Rows.Count);
        Assert.Equal("id", schema.Rows[0][SchemaTableColumn.ColumnName]);
    }

    [Fact]
    public async Task DataReader_SupportsAsyncEnumeration()
    {
        await using var connection = NewConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM t";

        await using var reader = (BricksNetDataReader)await command.ExecuteReaderAsync();

        var count = 0;
        await foreach (var record in reader)
        {
            Assert.NotNull(record.GetValue(record.GetOrdinal("id")));
            count++;
        }

        Assert.Equal(3, count);
    }

    [Fact]
    public void BeginTransaction_Throws_NotSupported()
    {
        using var connection = NewConnection();
        connection.Open();

        Assert.Throws<NotSupportedException>(() => connection.BeginTransaction());
    }

    [Fact]
    public void Parameter_NonInputDirection_Throws()
    {
        var parameter = new BricksNetParameter("p", 1);

        Assert.Throws<NotSupportedException>(() => parameter.Direction = ParameterDirection.Output);
    }

    [Fact]
    public void ProviderFactory_CreatesBricksNetTypes()
    {
        var factory = BricksNetProviderFactory.Instance;

        Assert.IsType<BricksNetConnection>(factory.CreateConnection());
        Assert.IsType<BricksNetCommand>(factory.CreateCommand());
        Assert.IsType<BricksNetParameter>(factory.CreateParameter());
        Assert.IsType<BricksNetConnectionStringBuilder>(factory.CreateConnectionStringBuilder());
    }
}
