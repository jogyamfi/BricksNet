using System.Data;
using BricksNet.AdoNet;
using Xunit;

namespace BricksNet.IntegrationTests.Live;

/// <summary>
/// ADO.NET provider tests against a real SQL warehouse. The DML test empties and refills
/// <c>dml_target</c>, so the class runs its tests one at a time.
/// </summary>
[Trait("Category", "E2E")]
[Collection(nameof(LiveAdoNetTests))]
public sealed class LiveAdoNetTests
{
    [LiveTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteReader_ReadsTypedValues(bool useThrift)
    {
        await using var connection = new BricksNetConnection(LiveWorkspace.ConnectionString(useThrift));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, name, active, created_at FROM people ORDER BY id";

        await using var reader = await command.ExecuteReaderAsync();

        Assert.Equal(4, reader.FieldCount);
        Assert.Equal("name", reader.GetName(1));
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1, reader.GetInt32(0));
        Assert.Equal("Ada", reader.GetString(1));
        Assert.True(reader.GetBoolean(2));
        Assert.Equal(new DateTime(2024, 1, 15, 9, 30, 0), reader.GetDateTime(3));

        var read = 1;
        while (await reader.ReadAsync())
        {
            read++;
            if (reader.GetInt32(0) == 5)
            {
                Assert.True(reader.IsDBNull(1));
            }
        }

        Assert.Equal(5, read);
    }

    [LiveTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteScalar_WithParameter(bool useThrift)
    {
        await using var connection = new BricksNetConnection(LiveWorkspace.ConnectionString(useThrift));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM people WHERE id = :id";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "id";
        parameter.DbType = DbType.Int32;
        parameter.Value = 3;
        command.Parameters.Add(parameter);

        Assert.Equal("Chiamaka", await command.ExecuteScalarAsync());
    }

    [LiveTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteNonQuery_ReportsRowsAffected(bool useThrift)
    {
        await using var connection = new BricksNetConnection(LiveWorkspace.ConnectionString(useThrift));
        await connection.OpenAsync();

        await ExecuteAsync(connection, "DELETE FROM dml_target");

        Assert.Equal(3, await ExecuteAsync(connection,
            "INSERT INTO dml_target VALUES (1, 'new', 10.00), (2, 'new', 20.00), (3, 'new', 30.00)"));
        Assert.Equal(2, await ExecuteAsync(connection, "UPDATE dml_target SET status = 'paid' WHERE id <= 2"));
        Assert.Equal(1, await ExecuteAsync(connection, "DELETE FROM dml_target WHERE id = 3"));
        Assert.Equal(2, await ExecuteAsync(connection, """
            MERGE INTO dml_target t
            USING (SELECT 2 AS id, 'void' AS status UNION ALL SELECT 4, 'new') s
            ON t.id = s.id
            WHEN MATCHED THEN UPDATE SET t.status = s.status
            WHEN NOT MATCHED THEN INSERT (id, status, amount) VALUES (s.id, s.status, 0)
            """));

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM dml_target";
        Assert.Equal(3, Convert.ToInt32(await command.ExecuteScalarAsync()));
    }

    [LiveTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task KeepSessionAlive_SurvivesCloseAndReopen(bool useThrift)
    {
        await using var connection = new BricksNetConnection(LiveWorkspace.ConnectionString(useThrift) + ";KeepSessionAlive=true");

        for (var i = 0; i < 3; i++)
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            Assert.Equal(1, Convert.ToInt32(await command.ExecuteScalarAsync()));
            await connection.CloseAsync();
        }
    }

    private static async Task<int> ExecuteAsync(BricksNetConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteNonQueryAsync();
    }
}
