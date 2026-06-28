using BricksNet.Contracts;
using BricksNet.Contracts.Options;
using Microsoft.Extensions.Logging;

namespace BricksNet.Auth;

/// <summary>
/// Creates the appropriate <see cref="IAuthProvider"/> for a set of client options.
/// </summary>
/// <remarks>
/// <see cref="AuthType.Pat"/> yields a <see cref="PatAuthProvider"/>; <see cref="AuthType.OAuthPkce"/>
/// yields a <see cref="CachedTokenProvider"/> wrapping an <see cref="OAuthPkceAuthProvider"/>.
/// Credentials are supplied as parameters rather than carried on
/// <see cref="BricksNetClientOptions"/> so they are not retained in long-lived option records.
/// </remarks>
public static class AuthProviderFactory
{
    /// <summary>Creates an <see cref="IAuthProvider"/> based on <paramref name="options"/>.</summary>
    /// <param name="options">Client options selecting the authentication mechanism.</param>
    /// <param name="accessToken">The personal access token. Required when using <see cref="AuthType.Pat"/>.</param>
    /// <param name="oauthOptions">OAuth configuration. Required when using <see cref="AuthType.OAuthPkce"/>.</param>
    /// <param name="tokenExchangeClient">
    /// HTTP client used for the OAuth token exchange. Required when using <see cref="AuthType.OAuthPkce"/>.
    /// Must not be the auth-injecting BricksNet pipeline client.
    /// </param>
    /// <param name="tokenCache">Optional token cache for the OAuth flow.</param>
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

                var oauthProvider = new OAuthPkceAuthProvider(
                    oauthOptions,
                    tokenExchangeClient,
                    tokenCache,
                    loggerFactory?.CreateLogger<OAuthPkceAuthProvider>());

                return new CachedTokenProvider(
                    oauthProvider,
                    loggerFactory?.CreateLogger<CachedTokenProvider>());

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(options), options.AuthType, "Unsupported authentication type.");
        }
    }
}
