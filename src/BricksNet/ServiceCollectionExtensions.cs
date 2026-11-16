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
    /// Registers the BricksNet HTTP pipeline, a personal-access-token (or OAuth client-credentials)
    /// <see cref="IAuthProvider"/>,
    /// and a singleton <see cref="IBricksNetClient"/>.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="options">Connection options.</param>
    /// <param name="accessToken">
    /// The personal access token for <see cref="AuthType.Pat"/>, or the client secret for
    /// <see cref="AuthType.OAuthClientCredentials"/> (with <see cref="BricksNetClientOptions.OAuthClientId"/>).
    /// </param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddBricksNet(
        this IServiceCollection services,
        BricksNetClientOptions options,
        string accessToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrEmpty(accessToken);

        bool needsTokenExchange = options.AuthType == AuthType.OAuthClientCredentials;
        if (needsTokenExchange)
        {
            AddTokenExchangeClient(services, options);
        }

        services.AddSingleton<IAuthProvider>(sp => AuthProviderFactory.Create(
            options,
            accessToken: accessToken,
            tokenExchangeClient: needsTokenExchange
                ? sp.GetRequiredService<IHttpClientFactory>().CreateClient(OAuthTokenExchangeClientName)
                : null,
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

        AddTokenExchangeClient(services, options);

        services.AddSingleton<IAuthProvider>(sp => AuthProviderFactory.Create(
            options,
            oauthOptions: oauthOptions,
            tokenExchangeClient: sp.GetRequiredService<IHttpClientFactory>()
                .CreateClient(OAuthTokenExchangeClientName),
            loggerFactory: sp.GetService<ILoggerFactory>()));

        return RegisterClient(services, options);
    }

    /// <summary>
    /// Registers the BricksNet HTTP pipeline, the <see cref="IAuthProvider"/> built by
    /// <paramref name="authProviderFactory"/>, and a singleton <see cref="IBricksNetClient"/>. Use this
    /// for credentials BricksNet does not obtain itself, such as Microsoft Entra ID tokens.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="options">Connection options. <see cref="BricksNetClientOptions.AuthType"/> is ignored.</param>
    /// <param name="authProviderFactory">
    /// Creates the singleton auth provider. Its <see cref="IAuthProvider.GetTokenAsync"/> is called for
    /// every HTTP request, so it should cache tokens and refresh them before they expire.
    /// </param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddBricksNet(
        this IServiceCollection services,
        BricksNetClientOptions options,
        Func<IServiceProvider, IAuthProvider> authProviderFactory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(authProviderFactory);

        services.AddSingleton(authProviderFactory);

        return RegisterClient(services, options);
    }

    private const string OAuthTokenExchangeClientName = "BricksNet.OAuthTokenExchange";

    /// <summary>
    /// Registers a dedicated client for OAuth token requests. It shares the pipeline's proxy and TLS
    /// settings but not its handlers, so no bearer token is attached to the token request itself.
    /// </summary>
    private static void AddTokenExchangeClient(IServiceCollection services, BricksNetClientOptions options) =>
        services.AddHttpClient(OAuthTokenExchangeClientName)
            .ConfigurePrimaryHttpMessageHandler(sp => Connection.HttpClientBuilderExtensions.CreatePrimaryHandler(
                options,
                sp.GetRequiredService<ILoggerFactory>().CreateLogger(OAuthTokenExchangeClientName)));

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
