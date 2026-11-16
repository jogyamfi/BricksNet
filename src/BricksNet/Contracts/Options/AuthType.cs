namespace BricksNet.Contracts.Options;

/// <summary>Authentication mechanism used to connect to Databricks.</summary>
public enum AuthType
{
    /// <summary>Personal Access Token authentication.</summary>
    Pat,

    /// <summary>OAuth 2.0 Authorization Code flow with PKCE (interactive, opens a browser).</summary>
    OAuthPkce,

    /// <summary>
    /// OAuth 2.0 client credentials (machine-to-machine) for a service principal. Set
    /// <see cref="BricksNetClientOptions.OAuthClientId"/> and supply the client secret where a
    /// personal access token would go.
    /// </summary>
    OAuthClientCredentials,
}
