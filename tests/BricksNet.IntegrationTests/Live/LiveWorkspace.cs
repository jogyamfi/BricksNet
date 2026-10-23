using BricksNet.Contracts.Options;
using Xunit;

namespace BricksNet.IntegrationTests.Live;

/// <summary>
/// Settings for the live workspace suite, read from environment variables. The fixture tables are
/// created by <c>scripts/databricks/01_setup.sql</c>; see <c>docs/LIVE_TESTING.md</c>.
/// </summary>
/// <remarks>
/// <list type="table">
/// <item><term><c>BRICKSNET_E2E_HOST</c></term><description>Workspace host, e.g. <c>dbc-1234abcd-5678.cloud.databricks.com</c> (required).</description></item>
/// <item><term><c>BRICKSNET_E2E_HTTP_PATH</c></term><description>SQL warehouse HTTP path, <c>/sql/1.0/warehouses/&lt;id&gt;</c> (required).</description></item>
/// <item><term><c>BRICKSNET_E2E_TOKEN</c></term><description>Personal access token (required).</description></item>
/// <item><term><c>BRICKSNET_E2E_CATALOG</c></term><description>Catalog holding the fixtures. Defaults to <c>workspace</c>.</description></item>
/// <item><term><c>BRICKSNET_E2E_SCHEMA</c></term><description>Schema holding the fixtures. Defaults to <c>bricksnet_live</c>.</description></item>
/// </list>
/// </remarks>
public static class LiveWorkspace
{
    public const string SkipReason =
        "Live workspace tests are skipped: set BRICKSNET_E2E_HOST, BRICKSNET_E2E_HTTP_PATH and BRICKSNET_E2E_TOKEN to run them.";

    public static string? Host => Environment.GetEnvironmentVariable("BRICKSNET_E2E_HOST");

    public static string? HttpPath => Environment.GetEnvironmentVariable("BRICKSNET_E2E_HTTP_PATH");

    public static string? Token => Environment.GetEnvironmentVariable("BRICKSNET_E2E_TOKEN");

    public static string Catalog => OrDefault(Environment.GetEnvironmentVariable("BRICKSNET_E2E_CATALOG"), "workspace");

    public static string Schema => OrDefault(Environment.GetEnvironmentVariable("BRICKSNET_E2E_SCHEMA"), "bricksnet_live");

    public static bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(HttpPath) && !string.IsNullOrWhiteSpace(Token);

    /// <summary>Fully qualified, backtick-delimited name of a fixture table.</summary>
    public static string Table(string name) => $"`{Catalog}`.`{Schema}`.`{name}`";

    public static BricksNetClient CreateClient(bool useThrift, bool useCloudFetch = false) =>
        new(
            new BricksNetClientOptions
            {
                Host = Host!,
                HttpPath = HttpPath!,
                UseThrift = useThrift,
                UseCloudFetch = useCloudFetch,
            },
            Token);

    public static string ConnectionString(bool useThrift) =>
        $"Host={Host};HttpPath={HttpPath};Token={Token};UseThrift={useThrift};Catalog={Catalog};Schema={Schema}";

    private static string OrDefault(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;
}

/// <summary>A theory that runs only when <see cref="LiveWorkspace.IsConfigured"/>.</summary>
public sealed class LiveTheoryAttribute : TheoryAttribute
{
    public LiveTheoryAttribute()
    {
        if (!LiveWorkspace.IsConfigured)
        {
            Skip = LiveWorkspace.SkipReason;
        }
    }
}
