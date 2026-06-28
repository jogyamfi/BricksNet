using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using BricksNet.Connection.Handlers;
using BricksNet.Contracts;
using BricksNet.Contracts.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BricksNet.Connection;

/// <summary>
/// Registers the BricksNet HTTP transport — the typed <see cref="BricksNetHttpClient"/>, its primary
/// <see cref="SocketsHttpHandler"/> (proxy + TLS), and the delegating-handler pipeline.
/// </summary>
public static class HttpClientBuilderExtensions
{
    /// <summary>
    /// Registers the BricksNet HTTP transport on <paramref name="services"/>.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="options">Client options carrying host, retry, proxy and TLS configuration.</param>
    /// <returns>The <see cref="IHttpClientBuilder"/> for further customisation.</returns>
    /// <remarks>
    /// An <see cref="IAuthProvider"/> must be registered separately (see Phase 4). The delegating
    /// handlers execute in the order <c>Retry → Auth</c>; retry is outermost so a fresh token is
    /// fetched on every replayed attempt. Proxy and TLS are configured on the primary handler.
    /// </remarks>
    public static IHttpClientBuilder AddBricksNetHttpClient(
        this IServiceCollection services,
        BricksNetClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        var clientOptions = new BricksNetHttpClientOptions
        {
            BaseAddress = new UriBuilder(Uri.UriSchemeHttps, options.Host, options.Port).Uri,
        };

        // Handlers are resolved per-request by IHttpClientFactory; register them as transient and
        // supply their non-DI configuration (RetryOptions) from the captured client options.
        services.AddTransient(sp => new HttpRetryHandler(
            options.RetryOptions,
            sp.GetRequiredService<ILogger<HttpRetryHandler>>()));
        services.AddTransient(sp => new AuthHeaderHandler(
            sp.GetRequiredService<IAuthProvider>(),
            sp.GetRequiredService<ILogger<AuthHeaderHandler>>()));

        IHttpClientBuilder builder = services
            .AddHttpClient<BricksNetHttpClient>(BricksNetHttpClient.HttpClientName, http =>
            {
                http.BaseAddress = clientOptions.BaseAddress;
                http.Timeout = clientOptions.Timeout;
                http.DefaultRequestHeaders.UserAgent.ParseAdd(clientOptions.UserAgent);
            })
            .ConfigurePrimaryHttpMessageHandler(sp =>
                CreatePrimaryHandler(options, sp.GetRequiredService<ILogger<BricksNetHttpClient>>()))
            // Order matters: the first handler added is outermost (executes first). Retry wraps Auth
            // so the token is re-fetched on every retry attempt.
            .AddHttpMessageHandler<HttpRetryHandler>()
            .AddHttpMessageHandler<AuthHeaderHandler>();

        return builder;
    }

    private static SocketsHttpHandler CreatePrimaryHandler(BricksNetClientOptions options, ILogger logger)
    {
        var handler = new SocketsHttpHandler
        {
            // Recycle pooled connections periodically so DNS / load-balancer changes are picked up.
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AutomaticDecompression = System.Net.DecompressionMethods.All,
        };

        IWebProxy? proxy = ProxyConfigHandler.CreateProxy(options.ProxyOptions);
        if (proxy is not null)
        {
            handler.Proxy = proxy;
            handler.UseProxy = true;
        }

        ConfigureTls(handler, options.SslOptions, logger);

        return handler;
    }

    private static void ConfigureTls(SocketsHttpHandler handler, SslOptions sslOptions, ILogger logger)
    {
        // Default: rely on the system trust store with full validation. No callback needed.
        if (!sslOptions.AllowSelfSignedCerts && sslOptions.CustomTrustStore is null)
        {
            return;
        }

        var sslClientOptions = new SslClientAuthenticationOptions();

        if (sslOptions.CustomTrustStore is { } customTrustStore)
        {
            if (sslOptions.AllowSelfSignedCerts)
            {
                logger.LogWarning(
                    "AllowSelfSignedCerts is enabled alongside a CustomTrustStore. "
                    + "Hostname verification will be skipped. Do not use in production.");
            }

            // Validate against the caller-supplied trust store instead of the machine store.
            var trustStore = new X509Certificate2Collection();
            trustStore.AddRange(customTrustStore);

            sslClientOptions.RemoteCertificateValidationCallback = (_, certificate, chain, sslPolicyErrors) =>
                ValidateAgainstCustomTrustStore(
                    certificate,
                    chain,
                    sslPolicyErrors,
                    trustStore,
                    sslOptions.AllowSelfSignedCerts);
        }
        else if (sslOptions.AllowSelfSignedCerts)
        {
            // WARNING: this path validates the certificate chain but skips hostname verification.
            // It weakens transport security and must only be used in development against known
            // endpoints. It NEVER unconditionally returns true.
            logger.LogWarning(
                "AllowSelfSignedCerts is enabled. Hostname verification will be skipped. "
                + "This weakens transport security and must not be used in production.");

            sslClientOptions.RemoteCertificateValidationCallback = (_, _, _, sslPolicyErrors) =>
                (sslPolicyErrors & ~SslPolicyErrors.RemoteCertificateNameMismatch) == SslPolicyErrors.None;
        }

        handler.SslOptions = sslClientOptions;
    }

    private static bool ValidateAgainstCustomTrustStore(
        X509Certificate? certificate,
        X509Chain? chain,
        SslPolicyErrors sslPolicyErrors,
        X509Certificate2Collection trustStore,
        bool allowSelfSignedCerts)
    {
        if (certificate is null || chain is null)
        {
            return false;
        }

        // A name mismatch is only tolerated when self-signed certs are explicitly allowed.
        SslPolicyErrors tolerated = allowSelfSignedCerts
            ? SslPolicyErrors.RemoteCertificateNameMismatch
            : SslPolicyErrors.None;
        if ((sslPolicyErrors & ~tolerated & ~SslPolicyErrors.RemoteCertificateChainErrors) != SslPolicyErrors.None)
        {
            return false;
        }

        using var cert2 = new X509Certificate2(certificate);
        var customChain = new X509Chain
        {
            ChainPolicy =
            {
                TrustMode = X509ChainTrustMode.CustomRootTrust,
                RevocationMode = X509RevocationMode.NoCheck,
            },
        };
        customChain.ChainPolicy.CustomTrustStore.AddRange(trustStore);

        try
        {
            return customChain.Build(cert2);
        }
        finally
        {
            customChain.Reset();
        }
    }
}
