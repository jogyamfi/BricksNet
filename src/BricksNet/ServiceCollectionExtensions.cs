using BricksNet.Auth;
using BricksNet.Connection;
using BricksNet.Contracts;
using BricksNet.Contracts.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BricksNet;

/// <summary>
/// Dependency-injection registration helpers for the BricksNet fluent API.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the BricksNet HTTP pipeline, a personal-access-token <see cref="IAuthProvider"/>,
    /// and a singleton <see cref="IBricksNetClient"/>.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="options">Connection options.</param>
    /// <param name="accessToken">The personal access token (required for <see cref="AuthType.Pat"/>).</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddBricksNet(
        this IServiceCollection services,
        BricksNetClientOptions options,
        string accessToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrEmpty(accessToken);

        services.AddSingleton<IAuthProvider>(sp => AuthProviderFactory.Create(
            options,
            accessToken: accessToken,
            loggerFactory: sp.GetService<ILoggerFactory>()));

        return RegisterClient(services, options);
    }

    /// <summary>
    /// Registers the BricksNet HTTP pipeline, an OAuth2 PKCE <see cref="IAuthProvider"/>, and a
    /// singleton <see cref="IBricksNetClient"/>.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="options">Connection options (set <see cref="BricksNetClientOptions.AuthType"/> to <see cref="AuthType.OAuthPkce"/>).</param>
    /// <param name="oauthOptions">OAuth client configuration.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddBricksNet(
        this IServiceCollection services,
        BricksNetClientOptions options,
        OAuthOptions oauthOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(oauthOptions);

        // A dedicated, non-pipeline client for the OAuth token exchange.
        services.AddHttpClient(OAuthTokenExchangeClientName);

        services.AddSingleton<IAuthProvider>(sp => AuthProviderFactory.Create(
            options,
            oauthOptions: oauthOptions,
            tokenExchangeClient: sp.GetRequiredService<IHttpClientFactory>()
                .CreateClient(OAuthTokenExchangeClientName),
            loggerFactory: sp.GetService<ILoggerFactory>()));

        return RegisterClient(services, options);
    }

    private const string OAuthTokenExchangeClientName = "BricksNet.OAuthTokenExchange";

    private static IServiceCollection RegisterClient(
        IServiceCollection services,
        BricksNetClientOptions options)
    {
        services.AddSingleton(options);
        services.AddBricksNetHttpClient(options);

        services.AddSingleton<IBricksNetClient>(sp => new BricksNetClient(
            options,
            sp.GetRequiredService<BricksNetHttpClient>(),
            sp.GetService<ILoggerFactory>()));

        return services;
    }
}
