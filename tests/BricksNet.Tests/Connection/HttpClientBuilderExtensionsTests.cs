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

    [Fact]
    public void AddBricksNetHttpClient_UseThrift_InjectsThriftUserAgent()
    {
        using var provider = BuildProvider(Options() with { UseThrift = true });

        var client = provider.GetRequiredService<BricksNetHttpClient>();

        Assert.Equal(BricksNetDefaults.ThriftUserAgent, client.DefaultRequestHeaders.UserAgent.ToString());
    }

    [Fact]
    public void AddBricksNetHttpClient_UseRest_InjectsLibraryDefaultUserAgent()
    {
        using var provider = BuildProvider(Options() with { UseThrift = false });

        var client = provider.GetRequiredService<BricksNetHttpClient>();

        Assert.StartsWith("BricksNet.dotnet/", client.DefaultRequestHeaders.UserAgent.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void AddBricksNetHttpClient_ExplicitUserAgent_UsesExplicitValue()
    {
        using var provider = BuildProvider(Options() with { UseThrift = true, UserAgent = "MyDriver/3.0.0" });

        var client = provider.GetRequiredService<BricksNetHttpClient>();

        Assert.Equal("MyDriver/3.0.0", client.DefaultRequestHeaders.UserAgent.ToString());
    }

    [Fact]
    public void AddBricksNetHttpClient_ExplicitSpaceFormatUserAgent_NormalizesToSlash()
    {
        using var provider = BuildProvider(Options() with { UserAgent = "SimbaJDBCDriver 2.6.38" });

        var client = provider.GetRequiredService<BricksNetHttpClient>();

        Assert.Equal("SimbaJDBCDriver/2.6.38", client.DefaultRequestHeaders.UserAgent.ToString());
    }

    private static X509Certificate2 CreateSelfSignedCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=bricksnet-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    }
}
