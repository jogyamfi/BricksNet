using System.Security.Cryptography.X509Certificates;

namespace BricksNet.Contracts.Options;

/// <summary>TLS/SSL configuration for outbound connections.</summary>
/// <remarks>
/// To connect through a TLS-inspecting proxy, or to an endpoint whose certificate is issued by a
/// private or self-signed root, put that root in <see cref="CustomTrustStore"/>. Do not use
/// <see cref="SkipHostnameVerification"/> for that: it does not make an untrusted certificate
/// acceptable.
/// </remarks>
public sealed record SslOptions
{
    /// <summary>
    /// When <see langword="true"/>, the server certificate chain is still validated, but a
    /// certificate issued for a different hostname is accepted.
    /// <para>
    /// <b>Warning:</b> this weakens transport security: any server holding a trusted certificate for
    /// any name can impersonate the workspace. Use only in development against known endpoints.
    /// </para>
    /// </summary>
    public bool SkipHostnameVerification { get; init; }

    /// <summary>
    /// When set, the server certificate must chain to one of these certificates. The system trust
    /// store is not used. Intermediates sent by the server are used to build the chain. Revocation
    /// is not checked.
    /// </summary>
    public X509Certificate2Collection? CustomTrustStore { get; init; }
}
