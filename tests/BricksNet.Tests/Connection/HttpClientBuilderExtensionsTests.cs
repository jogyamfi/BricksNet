using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using BricksNet.Contracts;
using BricksNet.Contracts.Options;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BricksNet.Connection;

public sealed class HttpClientBuilderExtensionsTests
{
    private sealed class StubAuthProvider : IAuthProvider
    {
        public Task<string> GetTokenAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult("token");
    }

    private static ServiceProvider BuildProvider(BricksNetClientOptions options)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IAuthProvider>(new StubAuthProvider());
        services.AddBricksNetHttpClient(options);
        return services.BuildServiceProvider();
    }

    private static BricksNetClientOptions Options(SslOptions? ssl = null) => new()
    {
        Host = "workspace.example.net",
        HttpPath = "/sql/1.0/warehouses/abc",
        SslOptions = ssl ?? new SslOptions(),
    };

    [Fact]
    public void AddBricksNetHttpClient_RegistersTypedClient_WithBaseAddress()
    {
        using var provider = BuildProvider(Options());

        var client = provider.GetRequiredService<BricksNetHttpClient>();

        Assert.NotNull(client.BaseAddress);
        Assert.Equal("workspace.example.net", client.BaseAddress!.Host);
        Assert.Equal(Uri.UriSchemeHttps, client.BaseAddress.Scheme);
    }

    [Fact]
    public void AddBricksNetHttpClient_AllowSelfSignedCerts_BuildsClient()
    {
        using var provider = BuildProvider(Options(new SslOptions { AllowSelfSignedCerts = true }));

        var client = provider.GetRequiredService<BricksNetHttpClient>();

        Assert.NotNull(client);
    }

    [Fact]
    public void AddBricksNetHttpClient_CustomTrustStore_BuildsClient()
    {
        using var cert = CreateSelfSignedCertificate();
        var trustStore = new X509Certificate2Collection { cert };

        using var provider = BuildProvider(Options(new SslOptions
        {
            CustomTrustStore = trustStore,
            AllowSelfSignedCerts = true,
        }));

        var client = provider.GetRequiredService<BricksNetHttpClient>();

        Assert.NotNull(client);
    }

    [Fact]
    public void AddBricksNetHttpClient_NullServices_Throws()
    {
        IServiceCollection services = null!;

        Assert.Throws<ArgumentNullException>(() => services.AddBricksNetHttpClient(Options()));
    }

    [Fact]
    public void AddBricksNetHttpClient_NullOptions_Throws()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(() => services.AddBricksNetHttpClient(null!));
    }

    private static X509Certificate2 CreateSelfSignedCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=bricksnet-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    }
}
