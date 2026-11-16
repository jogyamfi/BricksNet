using BricksNet.Contracts;
using BricksNet.Contracts.Options;
using Microsoft.Extensions.Logging;

namespace BricksNet.Auth;

/// <summary>
/// Creates the appropriate <see cref="IAuthProvider"/> for a set of client options.
/// </summary>
/// <remarks>
/// <see cref="AuthType.Pat"/> yields a <see cref="PatAuthProvider"/>; <see cref="AuthType.OAuthPkce"/>
/// and <see cref="AuthType.OAuthClientCredentials"/> yield a <see cref="CachedTokenProvider"/> wrapping
/// an <see cref="OAuthPkceAuthProvider"/> or an <see cref="OAuthClientCredentialsAuthProvider"/>.
/// Credentials are supplied as parameters rather than carried on
/// <see cref="BricksNetClientOptions"/> so they are not retained in long-lived option records.
/// </remarks>
internal static class AuthProviderFactory
{
    /// <summary>Creates an <see cref="IAuthProvider"/> based on <paramref name="options"/>.</summary>
    /// <param name="options">Client options selecting the authentication mechanism.</param>
    /// <param name="accessToken">
    /// The personal access token for <see cref="AuthType.Pat"/>, or the client secret for
    /// <see cref="AuthType.OAuthClientCredentials"/>.
    /// </param>
    /// <param name="oauthOptions">OAuth configuration. Required when using <see cref="AuthType.OAuthPkce"/>.</param>
    /// <param name="tokenExchangeClient">
    /// HTTP client used for the OAuth token exchange. Required for both OAuth types.
    /// Must not be the auth-injecting BricksNet pipeline client.
    /// </param>
    /// <param name="tokenCache">
    /// Optional token cache for the OAuth flow. When omitted and
    /// <see cref="OAuthOptions.PersistenceProvider"/> is set, an <see cref="OAuthTokenCache"/>
    /// backed by that provider is used.
    /// </param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    /// <returns>The configured <see cref="IAuthProvider"/>.</returns>
    public static IAuthProvider Create(
        BricksNetClientOptions options,
        string? accessToken = null,
        OAuthOptions? oauthOptions = null,
        HttpClient? tokenExchangeClient = null,
        ITokenCache? tokenCache = null,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        switch (options.AuthType)
        {
            case AuthType.Pat:
                if (string.IsNullOrEmpty(accessToken))
                {
                    throw new ArgumentException(
                        "A personal access token is required for AuthType.Pat.", nameof(accessToken));
                }

                return new PatAuthProvider(accessToken);

            case AuthType.OAuthPkce:
                ArgumentNullException.ThrowIfNull(oauthOptions);
                ArgumentNullException.ThrowIfNull(tokenExchangeClient);

                // Honour OAuthOptions.PersistenceProvider when no explicit cache is supplied, so
                // tokens survive process restarts.
                tokenCache ??= oauthOptions.PersistenceProvider is { } persistence
                    ? new OAuthTokenCache(persistence: persistence)
                    : null;

                var oauthProvider = new OAuthPkceAuthProvider(
                    oauthOptions,
                    tokenExchangeClient,
                    tokenCache,
                    loggerFactory?.CreateLogger<OAuthPkceAuthProvider>());

                return new CachedTokenProvider(
                    oauthProvider,
                    loggerFactory?.CreateLogger<CachedTokenProvider>());

            case AuthType.OAuthClientCredentials:
                if (string.IsNullOrEmpty(options.OAuthClientId))
                {
                    throw new ArgumentException(
                        "BricksNetClientOptions.OAuthClientId is required for AuthType.OAuthClientCredentials.",
                        nameof(options));
                }

                if (string.IsNullOrEmpty(accessToken))
                {
                    throw new ArgumentException(
                        "The OAuth client secret is required for AuthType.OAuthClientCredentials.",
                        nameof(accessToken));
                }

                ArgumentNullException.ThrowIfNull(tokenExchangeClient);

                var clientCredentialsProvider = new OAuthClientCredentialsAuthProvider(
                    OAuthClientCredentialsAuthProvider.TokenEndpointFor(options.Host, options.Port),
                    options.OAuthClientId,
                    accessToken,
                    tokenExchangeClient,
                    loggerFactory?.CreateLogger<OAuthClientCredentialsAuthProvider>());

                return new CachedTokenProvider(
                    clientCredentialsProvider,
                    loggerFactory?.CreateLogger<CachedTokenProvider>());

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(options), options.AuthType, "Unsupported authentication type.");
        }
    }
}
