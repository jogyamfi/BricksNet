using System.Reflection;

namespace BricksNet.Connection;

/// <summary>
/// Configuration for the <see cref="BricksNetHttpClient"/> and its underlying
/// <see cref="System.Net.Http.HttpClient"/> pipeline.
/// </summary>
internal sealed record BricksNetHttpClientOptions
{
    /// <summary>Base address for all requests (e.g. <c>https://my-workspace.azuredatabricks.net:443</c>).</summary>
    public required Uri BaseAddress { get; init; }

    /// <summary>Per-request timeout. Defaults to 100 seconds.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(100);

    /// <summary>
    /// Value sent in the <c>User-Agent</c> header. Defaults to
    /// <c>BricksNet.dotnet/&lt;assembly-version&gt;</c>.
    /// </summary>
    public string UserAgent { get; init; } = DefaultUserAgent;

    private static string DefaultUserAgent { get; } = BuildDefaultUserAgent();

    private static string BuildDefaultUserAgent()
    {
        Version? version = typeof(BricksNetHttpClientOptions).Assembly.GetName().Version;
        string versionText = typeof(BricksNetHttpClientOptions).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? version?.ToString()
            ?? "0.0.0";

        // Strip any build metadata (e.g. "+abc123") to keep the header clean.
        int plusIndex = versionText.IndexOf('+', StringComparison.Ordinal);
        if (plusIndex >= 0)
        {
            versionText = versionText[..plusIndex];
        }

        return $"BricksNet.dotnet/{versionText}";
    }
}
