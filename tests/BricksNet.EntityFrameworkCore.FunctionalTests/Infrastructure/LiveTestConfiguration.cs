using System.Globalization;
using Xunit;

namespace BricksNet.EntityFrameworkCore.FunctionalTests.Infrastructure;

/// <summary>
/// Connection settings for the live tests, read from environment variables. The tests are skipped
/// when any required variable is missing, so they never run (or fail) without a workspace.
/// </summary>
/// <remarks>
/// <list type="table">
/// <item><term><c>BRICKSNET_EF_HOST</c></term><description>Workspace host, e.g. <c>adb-123.azuredatabricks.net</c> (required).</description></item>
/// <item><term><c>BRICKSNET_EF_HTTPPATH</c></term><description>SQL warehouse or cluster HTTP path (required).</description></item>
/// <item><term><c>BRICKSNET_EF_TOKEN</c></term><description>Personal access token (required).</description></item>
/// <item><term><c>BRICKSNET_EF_CATALOG</c></term><description>Unity Catalog catalog in which a temporary schema is created (required).</description></item>
/// <item><term><c>BRICKSNET_EF_USETHRIFT</c></term><description><c>true</c> to use the Thrift backend (all-purpose clusters). Optional.</description></item>
/// </list>
/// </remarks>
public static class LiveTestConfiguration
{
    public const string SkipReason =
        "Live Databricks tests are skipped: set BRICKSNET_EF_HOST, BRICKSNET_EF_HTTPPATH, BRICKSNET_EF_TOKEN and BRICKSNET_EF_CATALOG to run them.";

    public static string? Host => Environment.GetEnvironmentVariable("BRICKSNET_EF_HOST");

    public static string? HttpPath => Environment.GetEnvironmentVariable("BRICKSNET_EF_HTTPPATH");

    public static string? Token => Environment.GetEnvironmentVariable("BRICKSNET_EF_TOKEN");

    public static string? Catalog => Environment.GetEnvironmentVariable("BRICKSNET_EF_CATALOG");

    public static bool UseThrift =>
        bool.TryParse(Environment.GetEnvironmentVariable("BRICKSNET_EF_USETHRIFT"), out var useThrift) && useThrift;

    public static bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(HttpPath)
        && !string.IsNullOrWhiteSpace(Token) && !string.IsNullOrWhiteSpace(Catalog);

    public static string ConnectionString =>
        string.Create(CultureInfo.InvariantCulture, $"Host={Host};HttpPath={HttpPath};Token={Token};Catalog={Catalog};UseThrift={UseThrift}");
}

/// <summary>A fact that runs only when <see cref="LiveTestConfiguration.IsConfigured"/>.</summary>
public sealed class LiveFactAttribute : FactAttribute
{
    public LiveFactAttribute()
    {
        if (!LiveTestConfiguration.IsConfigured)
        {
            Skip = LiveTestConfiguration.SkipReason;
        }
    }
}

/// <summary>A theory that runs only when <see cref="LiveTestConfiguration.IsConfigured"/>.</summary>
public sealed class LiveTheoryAttribute : TheoryAttribute
{
    public LiveTheoryAttribute()
    {
        if (!LiveTestConfiguration.IsConfigured)
        {
            Skip = LiveTestConfiguration.SkipReason;
        }
    }
}
