using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using BricksNet.Contracts.Options;
using Xunit;

namespace BricksNet.AdoNet;

public class BricksNetConnectionStringBuilderTests
{
    [Fact]
    public void TypedProperties_RoundTrip_ThroughConnectionString()
    {
        var builder = new BricksNetConnectionStringBuilder
        {
            Host = "workspace.azuredatabricks.net",
            Port = 443,
            HttpPath = "/sql/1.0/warehouses/abc",
            Token = "dapi-secret",
            AuthType = AuthType.Pat,
            UseThrift = false,
            Catalog = "main",
            Schema = "default",
            QueryTimeoutSeconds = 30,
            MaxRows = 5_000,
        };

        var roundTripped = new BricksNetConnectionStringBuilder(builder.ConnectionString);

        Assert.Equal("workspace.azuredatabricks.net", roundTripped.Host);
        Assert.Equal(443, roundTripped.Port);
        Assert.Equal("/sql/1.0/warehouses/abc", roundTripped.HttpPath);
        Assert.Equal("dapi-secret", roundTripped.Token);
        Assert.Equal(AuthType.Pat, roundTripped.AuthType);
        Assert.False(roundTripped.UseThrift);
        Assert.Equal("main", roundTripped.Catalog);
        Assert.Equal("default", roundTripped.Schema);
        Assert.Equal(30, roundTripped.QueryTimeoutSeconds);
        Assert.Equal(5_000, roundTripped.MaxRows);
    }

    [Fact]
    public void Defaults_AreApplied_WhenKeywordsAbsent()
    {
        var builder = new BricksNetConnectionStringBuilder();

        Assert.Equal(443, builder.Port);
        Assert.False(builder.UseThrift);
        Assert.Equal(AuthType.Pat, builder.AuthType);
        Assert.Equal(10_000, builder.MaxRows);
        Assert.Equal(0, builder.QueryTimeoutSeconds);
        Assert.False(builder.SkipHostnameVerification);
        Assert.Null(builder.TrustedCertificatesPath);
    }

    [Fact]
    public void ClientIdAndSecret_WithoutToken_InferOAuthClientCredentials()
    {
        var builder = new BricksNetConnectionStringBuilder("Host=h;HttpPath=/p;ClientId=sp-id;ClientSecret=sp-secret");

        Assert.Equal(AuthType.OAuthClientCredentials, builder.AuthType);

        var options = builder.ToClientOptions(out var credential);
        Assert.Equal(AuthType.OAuthClientCredentials, options.AuthType);
        Assert.Equal("sp-id", options.OAuthClientId);
        Assert.Equal("sp-secret", credential);
    }

    [Fact]
    public void Token_WinsOverClientSecret_WhenAuthTypeIsNotSet()
    {
        var builder = new BricksNetConnectionStringBuilder("Host=h;HttpPath=/p;Token=pat;ClientId=sp-id;ClientSecret=sp-secret");

        var options = builder.ToClientOptions(out var credential);

        Assert.Equal(AuthType.Pat, options.AuthType);
        Assert.Null(options.OAuthClientId);
        Assert.Equal("pat", credential);
    }

    [Fact]
    public void ExplicitOAuthClientCredentials_WithoutClientId_Throws()
    {
        var builder = new BricksNetConnectionStringBuilder(
            "Host=h;HttpPath=/p;AuthType=OAuthClientCredentials;ClientSecret=sp-secret");

        var ex = Assert.Throws<BricksNetException>(() => builder.ToClientOptions(out _));
        Assert.Contains("ClientId", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("OAuthClientId", "OAuthClientSecret")]
    [InlineData("Client Id", "Client Secret")]
    public void ClientCredentialAliases_Map(string idKeyword, string secretKeyword)
    {
        var builder = new BricksNetConnectionStringBuilder($"Host=h;HttpPath=/p;{idKeyword}=sp-id;{secretKeyword}=sp-secret");

        Assert.Equal("sp-id", builder.ClientId);
        Assert.Equal("sp-secret", builder.ClientSecret);
    }

    [Fact]
    public void ToClientOptions_NoTrustedCertificatesPath_LeavesCustomTrustStoreNull()
    {
        var builder = new BricksNetConnectionStringBuilder("Host=h;HttpPath=/p");

        Assert.Null(builder.ToClientOptions(out _).SslOptions.CustomTrustStore);
    }

    [Fact]
    public void ToClientOptions_TrustedCertificatesPath_LoadsPemCertificates()
    {
        string path = Path.GetTempFileName();
        try
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest(
                "CN=bricksnet-test-root", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using X509Certificate2 cert = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            File.WriteAllText(path, cert.ExportCertificatePem());

            var builder = new BricksNetConnectionStringBuilder(
                $"Host=h;HttpPath=/p;TrustedCertificatesPath={path}");
            X509Certificate2Collection? store = builder.ToClientOptions(out _).SslOptions.CustomTrustStore;

            Assert.NotNull(store);
            X509Certificate2 loaded = Assert.Single(store);
            Assert.Equal(cert.Thumbprint, loaded.Thumbprint);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ToClientOptions_TrustedCertificatesPathMissing_ThrowsBricksNetException()
    {
        string path = Path.Join(Path.GetTempPath(), $"bricksnet-missing-{Guid.NewGuid():N}.pem");
        var builder = new BricksNetConnectionStringBuilder($"Host=h;HttpPath=/p;TrustedCertificatesPath={path}");

        var ex = Assert.Throws<BricksNetException>(() => builder.ToClientOptions(out _));
        Assert.Contains("TrustedCertificatesPath", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ToClientOptions_TrustedCertificatesPathWithoutCertificates_ThrowsBricksNetException()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "not a certificate");
            var builder = new BricksNetConnectionStringBuilder(
                $"Host=h;HttpPath=/p;TrustedCertificatesPath={path}");

            Assert.Throws<BricksNetException>(() => builder.ToClientOptions(out _));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("Server")]
    [InlineData("Data Source")]
    [InlineData("Hostname")]
    public void HostAliases_MapToHost(string alias)
    {
        var builder = new BricksNetConnectionStringBuilder($"{alias}=my-host;HttpPath=/p");

        Assert.Equal("my-host", builder.Host);
    }

    [Theory]
    [InlineData("Password")]
    [InlineData("Access Token")]
    [InlineData("PWD")]
    public void TokenAliases_MapToToken(string alias)
    {
        var builder = new BricksNetConnectionStringBuilder($"Host=h;HttpPath=/p;{alias}=secret");

        Assert.Equal("secret", builder.Token);
    }

    [Fact]
    public void Database_Alias_MapsToSchema()
    {
        var builder = new BricksNetConnectionStringBuilder("Host=h;HttpPath=/p;Database=sales");

        Assert.Equal("sales", builder.Schema);
    }

    [Fact]
    public void ToClientOptions_ProjectsValues_AndExtractsToken()
    {
        var builder = new BricksNetConnectionStringBuilder
        {
            Host = "h",
            Port = 8443,
            HttpPath = "/sql/1.0/warehouses/x",
            Token = "tok",
            UseThrift = false,
            SkipHostnameVerification = true,
        };

        var options = builder.ToClientOptions(out var token);

        Assert.Equal("h", options.Host);
        Assert.Equal(8443, options.Port);
        Assert.Equal("/sql/1.0/warehouses/x", options.HttpPath);
        Assert.False(options.UseThrift);
        Assert.True(options.SslOptions.SkipHostnameVerification);
        Assert.Equal("tok", token);
    }

    [Fact]
    public void UseCloudFetch_ParsesFromConnectionString_AndProjects()
    {
        var builder = new BricksNetConnectionStringBuilder(
            "Host=h;HttpPath=/sql/1.0/warehouses/x;UseCloudFetch=true");

        Assert.True(builder.UseCloudFetch);
        Assert.True(builder.ToClientOptions(out _).UseCloudFetch);
        Assert.False(new BricksNetConnectionStringBuilder("Host=h;HttpPath=/p").UseCloudFetch);
    }

    [Fact]
    public void ToClientOptions_BuildsProxyOptions_WhenProxyUriSet()
    {
        var builder = new BricksNetConnectionStringBuilder
        {
            Host = "h",
            HttpPath = "/p",
            ProxyUri = "http://proxy.corp:8080",
            ProxyType = ProxyType.Http,
            ProxyUser = "user",
            ProxyPassword = "pass",
        };

        var options = builder.ToClientOptions(out _);

        Assert.NotNull(options.ProxyOptions);
        Assert.Equal(new Uri("http://proxy.corp:8080"), options.ProxyOptions!.Uri);
        Assert.Equal(ProxyType.Http, options.ProxyOptions.Type);
        Assert.NotNull(options.ProxyOptions.Credentials);
        Assert.Equal("user", options.ProxyOptions.Credentials!.UserName);
    }

    [Fact]
    public void ToSessionOptions_ProjectsCatalogSchemaAndLimits()
    {
        var builder = new BricksNetConnectionStringBuilder
        {
            Host = "h",
            HttpPath = "/p",
            Catalog = "main",
            Schema = "default",
            QueryTimeoutSeconds = 60,
            MaxRows = 2_500,
        };

        var session = builder.ToSessionOptions();

        Assert.Equal("main", session.Catalog);
        Assert.Equal("default", session.Schema);
        Assert.Equal(60, session.QueryTimeoutSeconds);
        Assert.Equal(2_500, session.MaxRows);
    }

    [Theory]
    [InlineData("HttpPath=/p")]
    [InlineData("Host=h")]
    public void ToClientOptions_Throws_WhenRequiredKeywordMissing(string connectionString)
    {
        var builder = new BricksNetConnectionStringBuilder(connectionString);

        Assert.Throws<BricksNetException>(() => builder.ToClientOptions(out _));
    }
}
