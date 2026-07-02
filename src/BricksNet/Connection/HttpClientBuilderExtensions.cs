using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
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
public static partial class HttpClientBuilderExtensions
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

        // Determine the effective User-Agent, auto-injecting a Thrift-compatible value when
        // no explicit override is provided, and normalizing the common space-separated format.
        string effectiveUserAgent;
        string? originalUserAgent = null;
        bool wasNormalized;

        if (options.UserAgent is { Length: > 0 } explicitUa)
        {
            // Explicit override: normalize space-separated 'Product Version' to RFC 7231
            // 'Product/Version' if needed (e.g. 'SimbaJDBCDriver 2.6.38' → 'SimbaJDBCDriver/2.6.38').
            effectiveUserAgent = NormalizeUserAgent(explicitUa, out wasNormalized);
            if (wasNormalized)
            {
                originalUserAgent = explicitUa;
            }
        }
        else if (options.UseThrift)
        {
            // Thrift (HiveServer2) endpoint enforces a Simba driver allow-list; auto-inject the
            // default value so callers do not need to configure it manually.
            effectiveUserAgent = BricksNetDefaults.ThriftUserAgent;
            wasNormalized = false;
        }
        else
        {
            // REST endpoint: use the library's own default identifier.
            effectiveUserAgent = clientOptions.UserAgent;
            wasNormalized = false;
        }

        // Handlers are resolved per-request by IHttpClientFactory; register them as transient and
        // supply their non-DI configuration (RetryOptions) from the captured client options.
        services.AddTransient(sp => new HttpRetryHandler(
            options.RetryOptions,
            sp.GetRequiredService<ILogger<HttpRetryHandler>>()));
        services.AddTransient(sp => new AuthHeaderHandler(
            sp.GetRequiredService<IAuthProvider>(),
            sp.GetRequiredService<ILogger<AuthHeaderHandler>>()));

        IHttpClientBuilder builder = services
            .AddHttpClient<BricksNetHttpClient>(BricksNetHttpClient.HttpClientName, (sp, http) =>
            {
                http.BaseAddress = clientOptions.BaseAddress;
                http.Timeout = clientOptions.Timeout;

                if (wasNormalized)
                {
                    sp.GetService<ILoggerFactory>()
                        ?.CreateLogger(nameof(BricksNetHttpClient))
                        .LogDebug(
                            "UserAgent value '{Original}' was normalized to '{Effective}'. "
                            + "Use 'Product/Version' format to avoid this.",
                            originalUserAgent,
                            effectiveUserAgent);
                }

                http.DefaultRequestHeaders.UserAgent.ParseAdd(effectiveUserAgent);
            })
            .ConfigurePrimaryHttpMessageHandler(sp =>
                CreatePrimaryHandler(options, sp.GetRequiredService<ILogger<BricksNetHttpClient>>()))
            // Order matters: the first handler added is outermost (executes first). Retry wraps Auth
            // so the token is re-fetched on every retry attempt.
            .AddHttpMessageHandler<HttpRetryHandler>()
            .AddHttpMessageHandler<AuthHeaderHandler>();

        return builder;
    }

    /// <summary>
    /// Normalizes a <c>User-Agent</c> value from the common space-separated <c>Product Version</c>
    /// format to the RFC 7231-compliant <c>Product/Version</c> format.
    /// </summary>
    /// <param name="ua">The raw User-Agent string (must be non-null).</param>
    /// <param name="wasNormalized">
    /// Set to <see langword="true"/> if a space-to-slash normalization was applied;
    /// <see langword="false"/> if the value was already well-formed.
    /// </param>
    /// <returns>The normalized User-Agent string, or the original value unchanged.</returns>
    internal static string NormalizeUserAgent(string ua, out bool wasNormalized)
    {
        var match = ProductVersionSpaceRegex().Match(ua);
        if (match.Success)
        {
            wasNormalized = true;
            return $"{match.Groups[1].Value}/{match.Groups[2].Value}";
        }

        wasNormalized = false;
        return ua;
    }

    /// <summary>Matches 'ProductName 1.2.3' (space-separated) but not 'ProductName/1.2.3'.</summary>
    [GeneratedRegex(@"^([A-Za-z][\w.-]*) (\d+\.\d+[\w.-]*)$", RegexOptions.CultureInvariant)]
    private static partial Regex ProductVersionSpaceRegex();

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
