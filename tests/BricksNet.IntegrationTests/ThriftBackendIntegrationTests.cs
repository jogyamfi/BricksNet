using System.Collections.Generic;
using System.Threading.Tasks;
using BricksNet.Contracts.Options;
using Xunit;

namespace BricksNet.IntegrationTests;

/// <summary>
/// End-to-end tests for the Thrift (TCLIService) backend.
/// </summary>
/// <remarks>
/// The Thrift wire protocol exchanges binary frames whose response structs are <c>internal</c> to
/// the <c>BricksNet</c> assembly and expose only client-side <c>ReadAsync</c>, so a faithful offline
/// fake server cannot be built from the test assembly without re-implementing server-side
/// serialisation. These tests therefore run against a live workspace and are excluded from the
/// default suite via <c>[Trait("Category", "E2E")]</c>. They self-skip when credentials are absent.
/// Provide <c>BRICKSNET_E2E_HOST</c>, <c>BRICKSNET_E2E_THRIFT_PATH</c> and <c>BRICKSNET_E2E_TOKEN</c>
/// to enable them: <c>dotnet test --filter "Category=E2E"</c>.
/// </remarks>
[Trait("Category", "E2E")]
public sealed class ThriftBackendIntegrationTests
{
    private static (string Host, string Path, string Token)? Credentials()
    {
        var host = Environment.GetEnvironmentVariable("BRICKSNET_E2E_HOST");
        var path = Environment.GetEnvironmentVariable("BRICKSNET_E2E_THRIFT_PATH");
        var token = Environment.GetEnvironmentVariable("BRICKSNET_E2E_TOKEN");

        if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(path) || string.IsNullOrEmpty(token))
        {
            return null;
        }

        return (host, path, token);
    }

    [Fact]
    public async Task ExecuteStatement_OverThrift_ReturnsRows()
    {
        if (Credentials() is not { } creds)
        {
            return; // No live credentials configured; nothing to assert offline.
        }

        var options = new BricksNetClientOptions
        {
            Host = creds.Host,
            HttpPath = creds.Path,
            UseThrift = true,
        };

        await using var client = new BricksNetClient(options, creds.Token);
        await using var session = await client.OpenSessionAsync();
        await using var operation = await session.ExecuteStatementAsync("SELECT 1 AS one");

        var rows = await operation.FetchAllAsync<IReadOnlyDictionary<string, object?>>();

        Assert.Single(rows);
        Assert.Equal(1, Convert.ToInt32(rows[0]["one"]));
    }

    [Fact]
    public async Task GetCatalogs_OverThrift_ReturnsAtLeastOneRow()
    {
        if (Credentials() is not { } creds)
        {
            return;
        }

        var options = new BricksNetClientOptions
        {
            Host = creds.Host,
            HttpPath = creds.Path,
            UseThrift = true,
        };

        await using var client = new BricksNetClient(options, creds.Token);
        await using var session = await client.OpenSessionAsync();
        await using var operation = await session.GetCatalogsAsync();

        var rows = await operation.FetchAllAsync<IReadOnlyDictionary<string, object?>>();

        Assert.NotEmpty(rows);
    }
}
