using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Xunit;

namespace BricksNet.Connection;

/// <summary>
/// Tests for <see cref="HttpClientBuilderExtensions.ValidateAgainstCustomTrustStore"/>, the TLS
/// callback used when <c>SslOptions.CustomTrustStore</c> is set.
/// </summary>
public sealed class CustomTrustStoreValidationTests
{
    private const SslPolicyErrors ChainErrors = SslPolicyErrors.RemoteCertificateChainErrors;

    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Fact]
    public void LeafIssuedByTrustedRoot_IsAccepted()
    {
        using X509Certificate2 root = CreateCa("CN=trusted-root");
        using X509Certificate2 leaf = CreateLeaf(root, "CN=workspace.example.net", Now.AddDays(-1), Now.AddDays(30));

        Assert.True(Validate(leaf, root));
    }

    [Fact]
    public void LeafIssuedByRootNotInStore_IsRejected()
    {
        using X509Certificate2 trusted = CreateCa("CN=trusted-root");
        using X509Certificate2 other = CreateCa("CN=other-root");
        using X509Certificate2 leaf = CreateLeaf(other, "CN=workspace.example.net", Now.AddDays(-1), Now.AddDays(30));

        Assert.False(Validate(leaf, trusted));
    }

    [Fact]
    public void ExpiredLeaf_IsRejected()
    {
        using X509Certificate2 root = CreateCa("CN=trusted-root");
        using X509Certificate2 leaf = CreateLeaf(root, "CN=workspace.example.net", Now.AddDays(-30), Now.AddDays(-1));

        Assert.False(Validate(leaf, root));
    }

    [Fact]
    public void LeafIssuedByIntermediateSentByServer_IsAccepted()
    {
        using X509Certificate2 root = CreateCa("CN=trusted-root");
        using X509Certificate2 intermediate = CreateCa("CN=intermediate", issuer: root);
        using X509Certificate2 leaf = CreateLeaf(intermediate, "CN=workspace.example.net", Now.AddDays(-1), Now.AddDays(30));

        // The server sends the intermediate in the handshake; the custom store holds only the root.
        using var serverChain = new X509Chain();
        serverChain.ChainPolicy.ExtraStore.Add(intermediate);

        Assert.True(HttpClientBuilderExtensions.ValidateAgainstCustomTrustStore(
            leaf, serverChain, ChainErrors, [root], skipHostnameVerification: false));
    }

    [Fact]
    public void CertificatesSentByServer_AreNotTrustAnchors()
    {
        using X509Certificate2 trusted = CreateCa("CN=trusted-root");
        using X509Certificate2 otherRoot = CreateCa("CN=other-root");
        using X509Certificate2 intermediate = CreateCa("CN=intermediate", issuer: otherRoot);
        using X509Certificate2 leaf = CreateLeaf(intermediate, "CN=workspace.example.net", Now.AddDays(-1), Now.AddDays(30));

        using var serverChain = new X509Chain();
        serverChain.ChainPolicy.ExtraStore.Add(intermediate);
        serverChain.ChainPolicy.ExtraStore.Add(otherRoot);

        Assert.False(HttpClientBuilderExtensions.ValidateAgainstCustomTrustStore(
            leaf, serverChain, ChainErrors, [trusted], skipHostnameVerification: false));
    }

    [Fact]
    public void NameMismatch_IsRejected_UnlessHostnameVerificationSkipped()
    {
        using X509Certificate2 root = CreateCa("CN=trusted-root");
        using X509Certificate2 leaf = CreateLeaf(root, "CN=other.example.net", Now.AddDays(-1), Now.AddDays(30));
        const SslPolicyErrors errors = ChainErrors | SslPolicyErrors.RemoteCertificateNameMismatch;

        using var strictChain = new X509Chain();
        Assert.False(HttpClientBuilderExtensions.ValidateAgainstCustomTrustStore(
            leaf, strictChain, errors, [root], skipHostnameVerification: false));

        using var lenientChain = new X509Chain();
        Assert.True(HttpClientBuilderExtensions.ValidateAgainstCustomTrustStore(
            leaf, lenientChain, errors, [root], skipHostnameVerification: true));
    }

    [Fact]
    public void MissingCertificate_IsRejected()
    {
        using X509Certificate2 root = CreateCa("CN=trusted-root");
        using var chain = new X509Chain();

        Assert.False(HttpClientBuilderExtensions.ValidateAgainstCustomTrustStore(
            null, chain, SslPolicyErrors.RemoteCertificateNotAvailable, [root], skipHostnameVerification: false));
    }

    private static bool Validate(X509Certificate2 leaf, X509Certificate2 trustedRoot)
    {
        using var chain = new X509Chain();
        return HttpClientBuilderExtensions.ValidateAgainstCustomTrustStore(
            leaf, chain, ChainErrors, [trustedRoot], skipHostnameVerification: false);
    }

    private static X509Certificate2 CreateCa(string subject, X509Certificate2? issuer = null)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        if (issuer is null)
        {
            return request.CreateSelfSigned(Now.AddDays(-365), Now.AddYears(1));
        }

        // A CA certificate must carry its private key so it can sign the leaf below.
        using X509Certificate2 signed = request.Create(issuer, Now.AddDays(-300), Now.AddMonths(6), NewSerial());
        return signed.CopyWithPrivateKey(key);
    }

    private static X509Certificate2 CreateLeaf(
        X509Certificate2 issuer,
        string subject,
        DateTimeOffset notBefore,
        DateTimeOffset notAfter)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        return request.Create(issuer, notBefore, notAfter, NewSerial());
    }

    private static byte[] NewSerial() => RandomNumberGenerator.GetBytes(16);
}
