using BricksNet;
using BricksNet.Auth;
using BricksNet.Contracts.Options;
using Microsoft.Extensions.Configuration;

namespace BricksNet.Samples.Demos;

/// <summary>
/// Demonstrates the BricksNet fluent API with OAuth 2.0 Authorization Code + PKCE flow.
/// The first call opens a browser window for the user to authorise the application.
/// </summary>
/// <remarks>
/// The README's quick-start shows a non-existent <c>OAuthClientCredentialsOptions</c> type.
/// The library implements PKCE (browser redirect) via <see cref="OAuthOptions"/>, not
/// machine-to-machine client credentials. Update the README OAuth section accordingly.
/// </remarks>
public static class FluentOAuthDemo
{
    public static async Task RunAsync(IConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var hostname      = config["Databricks:Host"]               ?? throw new InvalidOperationException("Databricks:Host is not configured.");
        var httpPath      = config["Databricks:HttpPath"]           ?? throw new InvalidOperationException("Databricks:HttpPath is not configured.");
        var clientId      = config["Databricks:OAuthClientId"]      ?? throw new InvalidOperationException("Databricks:OAuthClientId is not configured.");
        var authEndpoint  = config["Databricks:OAuthAuthEndpoint"]  ?? throw new InvalidOperationException("Databricks:OAuthAuthEndpoint is not configured.");
        var tokenEndpoint = config["Databricks:OAuthTokenEndpoint"] ?? throw new InvalidOperationException("Databricks:OAuthTokenEndpoint is not configured.");

        var options = new BricksNetClientOptions
        {
            Host      = hostname,
            HttpPath  = httpPath,
            AuthType  = AuthType.OAuthPkce,
            UseThrift = false,
        };

        var oauthOptions = new OAuthOptions
        {
            ClientId              = clientId,
            AuthorizationEndpoint = new Uri(authEndpoint),
            TokenEndpoint         = new Uri(tokenEndpoint),
            Scopes                = ["sql"],
        };

        Console.WriteLine("[FluentOAuth] A browser window will open for OAuth 2.0 authorization.");
        Console.WriteLine("[FluentOAuth] Connecting with OAuth PKCE...");
        await using var client = new BricksNetClient(options, oauthOptions);

        Console.WriteLine("[FluentOAuth] Opening session...");
        await using var session = await client.OpenSessionAsync();

        Console.WriteLine("[FluentOAuth] Executing query...");
        await using var operation = await session.ExecuteStatementAsync(
            "SELECT current_catalog() AS catalog, current_user() AS user");

        var rows = await operation.FetchAllAsync<IReadOnlyDictionary<string, object?>>();

        Console.WriteLine("[FluentOAuth] Results:");
        foreach (var row in rows)
        {
            foreach (var (key, value) in row)
            {
                Console.WriteLine($"  {key} = {value}");
            }
        }

        Console.WriteLine("[FluentOAuth] Done.");
    }
}
