using BricksNet.Auth;
using BricksNet.Contracts;
using BricksNet.Contracts.Options;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BricksNet.FluentApi;

public sealed class ServiceCollectionExtensionsTests
{
    private static BricksNetClientOptions RestOptions => new()
    {
        Host = "workspace.azuredatabricks.net",
        HttpPath = "/sql/1.0/warehouses/abc123",
        UseThrift = false,
    };

    // ---- PAT overload ----

    [Fact]
    public async Task AddBricksNet_Pat_RegistersIBricksNetClient()
    {
        var services = new ServiceCollection();
        services.AddBricksNet(RestOptions, "dapi-test-token");

        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IBricksNetClient>();

        Assert.NotNull(client);
        Assert.IsType<BricksNetClient>(client);
    }

    [Fact]
    public async Task AddBricksNet_Pat_ClientIsSingleton()
    {
        var services = new ServiceCollection();
        services.AddBricksNet(RestOptions, "dapi-test-token");

        await using var provider = services.BuildServiceProvider();
        var first = provider.GetRequiredService<IBricksNetClient>();
        var second = provider.GetRequiredService<IBricksNetClient>();

        Assert.Same(first, second);
    }

    [Fact]
    public void AddBricksNet_Pat_NullOptions_Throws()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentNullException>(() =>
            services.AddBricksNet(null!, "dapi-token"));
    }

    [Fact]
    public void AddBricksNet_Pat_NullOrEmptyToken_Throws()
    {
        var services = new ServiceCollection();
        Assert.ThrowsAny<ArgumentException>(() =>
            services.AddBricksNet(RestOptions, string.Empty));
    }

    [Fact]
    public void AddBricksNet_Pat_NullServices_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ServiceCollectionExtensions.AddBricksNet(null!, RestOptions, "token"));
    }

    // ---- IAuthProvider registration ----

    [Fact]
    public async Task AddBricksNet_Pat_RegistersIAuthProvider()
    {
        var services = new ServiceCollection();
        services.AddBricksNet(RestOptions, "dapi-test-token");

        await using var provider = services.BuildServiceProvider();
        var authProvider = provider.GetRequiredService<BricksNet.Contracts.IAuthProvider>();

        Assert.NotNull(authProvider);
    }

    // ---- BricksNetClientOptions registration ----

    [Fact]
    public async Task AddBricksNet_Pat_RegistersClientOptions()
    {
        var services = new ServiceCollection();
        services.AddBricksNet(RestOptions, "dapi-test-token");

        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<BricksNetClientOptions>();

        Assert.Equal("workspace.azuredatabricks.net", options.Host);
    }

    // ---- OAuth overload validation ----

    [Fact]
    public void AddBricksNet_OAuth_NullOauthOptions_Throws()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentNullException>(() =>
            services.AddBricksNet(RestOptions, (OAuthOptions)null!));
    }

    [Fact]
    public async Task AddBricksNet_OAuth_RegistersIBricksNetClient()
    {
        var oauthOptions = new OAuthOptions
        {
            ClientId = "client-id",
            AuthorizationEndpoint = new Uri("https://login.microsoftonline.com/oauth2/authorize"),
            TokenEndpoint = new Uri("https://login.microsoftonline.com/oauth2/token"),
        };

        var services = new ServiceCollection();
        services.AddBricksNet(RestOptions with { AuthType = AuthType.OAuthPkce }, oauthOptions);

        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IBricksNetClient>();

        Assert.NotNull(client);
        Assert.IsType<BricksNetClient>(client);
    }
}
