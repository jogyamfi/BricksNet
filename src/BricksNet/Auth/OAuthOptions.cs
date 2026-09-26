namespace BricksNet.Auth;

/// <summary>
/// Configuration for the OAuth 2.0 Authorization Code flow with PKCE (RFC 7636) used by
/// <see cref="OAuthPkceAuthProvider"/>.
/// </summary>
public sealed record OAuthOptions
{
    /// <summary>Public OAuth client identifier registered with the authorization server.</summary>
    public required string ClientId { get; init; }

    /// <summary>HTTPS authorization endpoint the user's browser is redirected to.</summary>
    public required Uri AuthorizationEndpoint { get; init; }

    /// <summary>HTTPS token endpoint used to exchange the authorization code for tokens.</summary>
    public required Uri TokenEndpoint { get; init; }

    /// <summary>Scopes requested during authorization. Defaults to an empty set.</summary>
    public IReadOnlyList<string> Scopes { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Host name used for the loopback redirect listener and the <c>redirect_uri</c>. Defaults to
    /// <c>localhost</c>, which matches the redirect URIs registered for Databricks' built-in OAuth
    /// applications and can be bound by <see cref="System.Net.HttpListener"/> on Windows without
    /// administrator rights or a URL ACL reservation. Must be <c>localhost</c> or a loopback address.
    /// </summary>
    public string RedirectHost { get; init; } = "localhost";

    /// <summary>
    /// First loopback port tried for the redirect listener. Defaults to <c>8020</c>. If the port is
    /// busy, ports up to <see cref="RedirectPortScanLimit"/> are tried in turn.
    /// </summary>
    public int RedirectPort { get; init; } = 8020;

    /// <summary>Inclusive upper bound of the redirect-port fallback scan. Defaults to <c>8029</c>.</summary>
    public int RedirectPortScanLimit { get; init; } = 8029;

    /// <summary>Optional persistence provider for caching tokens across process restarts.</summary>
    public IOAuthPersistence? PersistenceProvider { get; init; }
}
