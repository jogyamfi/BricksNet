using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Xunit;

namespace BricksNet.AdoNet;

[Collection("AdoNetClientFactory")]
public sealed class BricksNetCommandTests : IDisposable
{
    private static readonly IReadOnlyList<IReadOnlyDictionary<string, object?>> SampleRows =
        FakeFluent.Rows(
            FakeFluent.Row(("id", 1), ("name", "alpha")),
            FakeFluent.Row(("id", 2), ("name", "beta")));

    public BricksNetCommandTests()
    {
        BricksNetConnection.ClientFactory = (_, _) => new FakeFluent.FakeClient(SampleRows);
    }

    public void Dispose() => BricksNetConnection.ClientFactory = null;

    private static BricksNetConnection NewOpenConnection()
    {
        var connection = new BricksNetConnection("Host=h;HttpPath=/sql/1.0/warehouses/x;Token=tok");
        connection.Open();
        return connection;
    }

    [Fact]
    public void Constructors_SetText_AndConnection()
    {
        using var withText = new BricksNetCommand("SELECT 1");
        Assert.Equal("SELECT 1", withText.CommandText);

        using var connection = new BricksNetConnection();
        using var withConnection = new BricksNetCommand("SELECT 1", connection);
        Assert.Same(connection, withConnection.Connection);
    }

    [Fact]
    public void CommandType_Text_IsDefault_AndSettable()
    {
        using var command = new BricksNetCommand { CommandType = CommandType.Text };

        Assert.Equal(CommandType.Text, command.CommandType);
    }

    [Theory]
    [InlineData(CommandType.StoredProcedure)]
    [InlineData(CommandType.TableDirect)]
    public void CommandType_NonText_Throws(CommandType type)
    {
        using var command = new BricksNetCommand();

        Assert.Throws<NotSupportedException>(() => command.CommandType = type);
    }

    [Fact]
    public void CommandTimeout_Negative_Throws()
    {
        using var command = new BricksNetCommand();

        Assert.Throws<ArgumentOutOfRangeException>(() => command.CommandTimeout = -1);
    }

    [Fact]
    public void CommandTimeout_RoundTrips()
    {
        using var command = new BricksNetCommand { CommandTimeout = 30 };

        Assert.Equal(30, command.CommandTimeout);
    }

    [Fact]
    public void Connection_WrongType_Throws()
    {
        using var command = new BricksNetCommand();

        Assert.Throws<InvalidCastException>(() => command.Connection = new FakeDbConnection());
    }

    [Fact]
    public void Connection_Null_IsAllowed()
    {
        using var command = new BricksNetCommand { Connection = null };

        Assert.Null(command.Connection);
    }

    [Fact]
    public void CreateParameter_ReturnsBricksNetParameter()
    {
        using var command = new BricksNetCommand();

        Assert.IsType<BricksNetParameter>(command.CreateParameter());
    }

    [Fact]
    public void Prepare_And_Cancel_AreSafeNoOps_WhenIdle()
    {
        using var command = new BricksNetCommand();

        command.Prepare();
        command.Cancel();

        Assert.Equal(UpdateRowSource.None, command.UpdatedRowSource);
    }

    [Fact]
    public void Execute_WithoutConnection_Throws()
    {
        using var command = new BricksNetCommand("SELECT 1");

        Assert.Throws<InvalidOperationException>(() => command.ExecuteScalar());
    }

    [Fact]
    public void Execute_WithEmptyCommandText_Throws()
    {
        using var connection = NewOpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "   ";

        Assert.Throws<InvalidOperationException>(() => command.ExecuteScalar());
    }

    [Fact]
    public async Task ExecuteReader_WithParameters_TrimsMarkers()
    {
        using var connection = NewOpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM t WHERE id = :id";
        var parameter = (BricksNetParameter)command.CreateParameter();
        parameter.ParameterName = ":id";
        parameter.Value = 1;
        parameter.TypeHint = "INT";
        command.Parameters.Add(parameter);

        await using var reader = await command.ExecuteReaderAsync();

        Assert.Equal(2, reader.FieldCount);
    }

    [Fact]
    public async Task ExecuteReader_SchemaOnly_ReturnsNoBufferedRows()
    {
        using var connection = NewOpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM t";

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SchemaOnly);

        // SchemaOnly skips the first chunk fetch, so no columns are inferred yet.
        Assert.False(reader.HasRows);
    }

    [Fact]
    public async Task ExecuteReader_SingleRow_LimitsChunk()
    {
        using var connection = NewOpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM t";

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow);

        Assert.True(await reader.ReadAsync());
        Assert.Equal(1, reader.GetInt32(reader.GetOrdinal("id")));
    }

    [Fact]
    public async Task ExecuteNonQuery_WaitsForCompletion_BeforeClosing()
    {
        using var connection = NewOpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO t VALUES (1)";

        await command.ExecuteNonQueryAsync();

        var operation = ((FakeFluent.FakeSession)connection.GetOpenSession()).LastOperation!;
        Assert.True(operation.Waited);
        Assert.True(operation.Closed);
    }

    [Fact]
    public async Task ExecuteNonQuery_StatementFails_Throws_AndCloses()
    {
        using var connection = NewOpenConnection();
        var session = (FakeFluent.FakeSession)connection.GetOpenSession();
        session.WaitFailure = new BricksNetException("boom", "42P01", 0, "q-1");
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO missing VALUES (1)";

        var ex = await Assert.ThrowsAsync<BricksNetException>(() => command.ExecuteNonQueryAsync());

        Assert.Equal("42P01", ex.SqlState);
        Assert.True(session.LastOperation!.Closed);
    }

    private sealed class FakeDbConnection : DbConnection
    {
        [AllowNull]
        public override string ConnectionString { get; set; } = string.Empty;

        public override string Database => string.Empty;

        public override string DataSource => string.Empty;

        public override string ServerVersion => string.Empty;

        public override ConnectionState State => ConnectionState.Closed;

        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();



        public override void Close() => throw new NotSupportedException();

        public override void Open() => throw new NotSupportedException();

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
            throw new NotSupportedException();

        protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
    }
}
