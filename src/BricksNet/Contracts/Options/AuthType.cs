namespace BricksNet.Contracts.Options;

/// <summary>Authentication mechanism used to connect to Databricks.</summary>
public enum AuthType
{
    /// <summary>Personal Access Token authentication.</summary>
    Pat,

    /// <summary>OAuth 2.0 Authorization Code flow with PKCE.</summary>
    OAuthPkce,
}
