using System.Collections.Generic;
using System.Threading.Tasks;
using BricksNet.AdoNet;
using BricksNet.Connection;
using BricksNet.Contracts;
using BricksNet.IntegrationTests.FakeService;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BricksNet.IntegrationTests;

/// <summary>
/// Replay integration tests for the ADO.NET provider, exercising the full
/// <see cref="BricksNetConnection"/> → <see cref="BricksNetCommand"/> → <see cref="BricksNetDataReader"/>
/// stack over the real fluent client and the in-process fake service.
/// </summary>
public sealed class AdoNetIntegrationTests : IntegrationTestBase, IDisposable
{
    private readonly Func<Contracts.Options.BricksNetClientOptions, string?, IBricksNetClient>? _previousFactory;

    public AdoNetIntegrationTests(FakeServiceFixture fixture)
        : base(fixture)
    {
        _previousFactory = BricksNetConnection.ClientFactory;

        // Route the ADO.NET connection's underlying fluent client at the fake REST server.
        BricksNetConnection.ClientFactory = (options, _) =>
        {
            var inner = new HttpClient { BaseAddress = new Uri(Fixture.BaseUrl) };
            var bricksHttp = new BricksNetHttpClient(inner, NullLogger<BricksNetHttpClient>.Instance);
            return new BricksNetClient(options with { UseThrift = false }, bricksHttp);
        };
    }

    public void Dispose()
    {
        BricksNetConnection.ClientFactory = _previousFactory;
        DisposeClients();
    }

    private static BricksNetConnection NewConnection() =>
        new("Host=fake.local;HttpPath=/sql/1.0/warehouses/fake;Token=tok;Catalog=main;Schema=default");

    [Fact]
    public async Task ExecuteReader_ReadsArrowRows_AndColumnMetadata()
    {
        LoadFixtures("adonet", "execute_reader");

        await using var connection = NewConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM sample";

        await using var reader = await command.ExecuteReaderAsync();

        Assert.Equal(3, reader.FieldCount);
        Assert.Equal("id", reader.GetName(0));
        Assert.Equal("name", reader.GetName(1));
        Assert.Equal("active", reader.GetName(2));

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
        LoadFixtures("adonet", "execute_reader");

        await using var connection = NewConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM sample";

        await using var reader = await command.ExecuteReaderAsync();
        var nameOrdinal = reader.GetOrdinal("name");

        Assert.True(await reader.ReadAsync());
        Assert.Equal("alpha", reader.GetString(nameOrdinal));

        Assert.True(await reader.ReadAsync());
        Assert.True(await reader.ReadAsync());
        Assert.True(reader.IsDBNull(nameOrdinal));
    }

    [Fact]
    public async Task ExecuteNonQuery_Dml_ReturnsAffectedRowsFromSummary()
    {
        LoadFixtures("adonet", "execute_dml");

        await using var connection = NewConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO sample VALUES (4, 'delta', true), (5, 'epsilon', false)";

        Assert.Equal(2, await command.ExecuteNonQueryAsync());
    }
}
