using System.Security.Cryptography.X509Certificates;

namespace BricksNet.Contracts.Options;

/// <summary>TLS/SSL configuration for outbound connections.</summary>
public sealed record SslOptions
{
    /// <summary>
    /// When <see langword="true"/>, the server certificate chain is validated
    /// but hostname verification is skipped.
    /// <para>
    /// <b>Warning:</b> enabling this option weakens transport security.
    /// Use only in development environments against known-safe endpoints.
    /// </para>
    /// </summary>
    public bool AllowSelfSignedCerts { get; init; }

    /// <summary>
    /// If set, the connection is validated against this trust store instead of
    /// the system trust store.
    /// </summary>
    public X509Certificate2Collection? CustomTrustStore { get; init; }
}
