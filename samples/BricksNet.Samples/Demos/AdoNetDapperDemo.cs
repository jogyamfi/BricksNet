using BricksNet.AdoNet;
using BricksNet.Contracts.Options;
using Dapper;
using Microsoft.Extensions.Configuration;

namespace BricksNet.Samples.Demos;

/// <summary>
/// Demonstrates the BricksNet ADO.NET provider used with Dapper —
/// both dynamic (anonymous) and typed POCO result mapping.
/// </summary>
public static class AdoNetDapperDemo
{
    public static async Task RunAsync(IConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var hostname = config["Databricks:Host"]     ?? throw new InvalidOperationException("Databricks:Host is not configured.");
        var httpPath = config["Databricks:HttpPath"] ?? throw new InvalidOperationException("Databricks:HttpPath is not configured.");
        var token    = config["Databricks:Token"]    ?? throw new InvalidOperationException("Databricks:Token is not configured.");
        var userAgent = config["Databricks:UserAgent"];  // optional override of default User-Agent header

        var options = new BricksNetClientOptions
        {
            Host = hostname,
            HttpPath = httpPath,
            UseThrift = true,
            UserAgent = string.IsNullOrWhiteSpace(userAgent) ? null : userAgent,
        };

        Console.WriteLine($"[AdoNetDapper] Connecting to Host={hostname};HttpPath={httpPath}");
        Console.WriteLine("[AdoNetDapper] Opening connection...");

        await using var conn = new BricksNetConnection(options, accessToken: token);
       
        await conn.OpenAsync();
        Console.WriteLine("[AdoNetDapper] Connected.");

        // --- Dynamic rows ---
        Console.WriteLine("[AdoNetDapper] Dynamic query: current catalog and database");
         var dynamics = await conn.QueryAsync(
             "SELECT current_catalog() AS catalog_name, current_database() AS schema_name");
        
        //var dynamics = await conn.QueryAsync(
        //    "select 1 as Option1 union select 2 as Option1");
        
        foreach (var row in dynamics)
        {
            Console.WriteLine($"  Option1={row.Option1}");
        }

        // --- Typed POCO via Dapper ---
        // Dapper maps result column names to public settable properties (case-insensitive).
        Console.WriteLine("[AdoNetDapper] Typed query: SHOW CATALOGS");
        var catalogs = await conn.QueryAsync<CatalogRow>("SHOW CATALOGS");
        foreach (var cat in catalogs)
        {
            Console.WriteLine($"  {cat.Catalog}");
        }

        Console.WriteLine("[AdoNetDapper] Done.");
    }

    // Column name returned by SHOW CATALOGS is "catalog"; Dapper matches case-insensitively.
    private sealed class CatalogRow
    {
        public string Catalog { get; set; } = string.Empty;
    }
}
