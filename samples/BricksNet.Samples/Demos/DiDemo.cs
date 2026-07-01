using BricksNet;
using BricksNet.Contracts;
using BricksNet.Contracts.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BricksNet.Samples.Demos;

/// <summary>
/// Demonstrates registering BricksNet into a .NET Generic Host via
/// <see cref="ServiceCollectionExtensions.AddBricksNet"/> and injecting
/// <see cref="IBricksNetClient"/> into a service class.
/// </summary>
public static class DiDemo
{
    public static async Task RunAsync(IConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var hostname = config["Databricks:Host"]     ?? throw new InvalidOperationException("Databricks:Host is not configured.");
        var httpPath = config["Databricks:HttpPath"] ?? throw new InvalidOperationException("Databricks:HttpPath is not configured.");
        var token    = config["Databricks:Token"]    ?? throw new InvalidOperationException("Databricks:Token is not configured.");

        var clientOptions = new BricksNetClientOptions
        {
            Host      = hostname,
            HttpPath  = httpPath,
            UseThrift = false,
        };

        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddBricksNet(clientOptions, accessToken: token);
                services.AddTransient<QueryService>();
            })
            .Build();

        Console.WriteLine("[Di] Resolving QueryService from DI container...");
        var queryService = host.Services.GetRequiredService<QueryService>();
        await queryService.RunAsync();

        Console.WriteLine("[Di] Done.");
    }

    private sealed class QueryService
    {
        private readonly IBricksNetClient _client;

        public QueryService(IBricksNetClient client) => _client = client;

        public async Task RunAsync()
        {
            Console.WriteLine("[Di] Opening session via injected IBricksNetClient...");
            await using var session = await _client.OpenSessionAsync();

            Console.WriteLine("[Di] Executing query...");
            await using var operation = await session.ExecuteStatementAsync(
                "SELECT current_catalog() AS catalog, current_user() AS user");

            var rows = await operation.FetchAllAsync<IReadOnlyDictionary<string, object?>>();

            Console.WriteLine("[Di] Results:");
            foreach (var row in rows)
            {
                Console.WriteLine($"  catalog={row["catalog"]}, user={row["user"]}");
            }
        }
    }
}
