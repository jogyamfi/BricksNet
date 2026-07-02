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
        Assert.False(builder.AllowSelfSignedCerts);
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
            AllowSelfSignedCerts = true,
        };

        var options = builder.ToClientOptions(out var token);

        Assert.Equal("h", options.Host);
        Assert.Equal(8443, options.Port);
        Assert.Equal("/sql/1.0/warehouses/x", options.HttpPath);
        Assert.False(options.UseThrift);
        Assert.True(options.SslOptions.AllowSelfSignedCerts);
        Assert.Equal("tok", token);
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
