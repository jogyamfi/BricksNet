using BricksNet;
using BricksNet.Contracts.Options;
using Microsoft.Extensions.Configuration;

namespace BricksNet.Samples.Demos;

/// <summary>
/// Demonstrates the BricksNet fluent API with Personal Access Token (PAT) authentication.
/// </summary>
public static class FluentPatDemo
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
            Host      = hostname,
            HttpPath  = httpPath,
            UseThrift = false,   // REST backend — targets SQL Warehouses
            UserAgent = userAgent,  // optional override of default User-Agent header
        };

        string sql = @"SELECT
  `ti`.`Id`,
  `ti`.`Name` AS InterventionName,
  `ti`.`ProblemOpportunity`,
  `ti`.`Intervention` AS InterventionDescription,
  `ti`.`Outcomes`,
  `ti`.`CohortDefinition`,
  `ti`.`Literature`,
  `ti`.`UIGroupData`,
  COUNT(`tiPmi`.`PseudoNHSNumber`) AS PatientCount
FROM
  Pathfinder.tbl_odin_p6_analytics_s2a_pophealthpmi_tableau AS `pmi`
    INNER JOIN pathfinder.`TargetedInterventionPMI` AS `tiPmi`
      ON `pmi`.`PseudoNHSNumber` = `tiPmi`.`PseudoNHSNumber`
      AND `pmi`.`SystemCode` = `tiPmi`.`SystemCode`
    INNER JOIN pathfinder.`TargetedIntervention` AS `ti`
      ON `tiPmi`.`TargetedIntervention_ID` = `ti`.`Id`
      AND `ti`.`Enabled` = 1
WHERE
  pmi.`Tenant_ID` = '1'
  AND pmi.`SystemCode` = 'ICS1'
  AND pmi.`ReportingPeriod_Name` = 'Jan25-Dec25'
  AND IsPathway = 1
GROUP BY
  `ti`.`Id`,
  `ti`.`Name`,
  `ti`.`ProblemOpportunity`,
  `ti`.`Intervention`,
  `ti`.`Outcomes`,
  `ti`.`CohortDefinition`,
  `ti`.`Literature`,
  `ti`.`UIGroupData`
ORDER BY
  `ti`.`Id`";

         sql = "select 1 as Option1 union select 2 as Option1";
        Console.WriteLine("[FluentPat] Connecting with PAT...");
        await using var client = new BricksNetClient(options, accessToken: token);

        Console.WriteLine("[FluentPat] Opening session...");
        await using var session = await client.OpenSessionAsync();

        Console.WriteLine("[FluentPat] Executing query...");
        await using var operation = await session.ExecuteStatementAsync(sql);
          //  "SELECT current_catalog() AS catalog, current_database() AS db");

        var rows = await operation.FetchAllAsync<IReadOnlyDictionary<string, object?>>();

        Console.WriteLine("[FluentPat] Results:");
        foreach (var row in rows)
        {
            foreach (var (key, value) in row)
            {
                Console.WriteLine($"  {key} = {value}");
            }
        }

        Console.WriteLine("[FluentPat] Done.");
    }
}
