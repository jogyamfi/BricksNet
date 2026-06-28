using System.Net.Http;
using BricksNet.Auth;
using BricksNet.Contracts.Options;
using Xunit;

namespace BricksNet.Auth;

public class AuthProviderFactoryTests
{
    private static OAuthOptions CreateOAuthOptions() => new()
    {
        ClientId = "client-1",
        AuthorizationEndpoint = new Uri("https://auth.example.com/authorize"),
        TokenEndpoint = new Uri("https://auth.example.com/token"),
    };

    [Fact]
    public void Create_ReturnsPatProvider_ForPatAuthType()
    {
        // Arrange
        var options = new BricksNetClientOptions
        {
            Host = "host.example.com",
            HttpPath = "/sql/1.0/warehouses/abc",
            AuthType = AuthType.Pat,
        };

        // Act
        var provider = AuthProviderFactory.Create(options, accessToken: "dapi-token");

        // Assert
        Assert.IsType<PatAuthProvider>(provider);
    }

    [Fact]
    public void Create_Throws_WhenPatTokenMissing()
    {
        // Arrange
        var options = new BricksNetClientOptions
        {
            Host = "host.example.com",
            HttpPath = "/sql/1.0/warehouses/abc",
            AuthType = AuthType.Pat,
        };

        // Act / Assert
        Assert.Throws<ArgumentException>(() => AuthProviderFactory.Create(options, accessToken: null));
    }

    [Fact]
    public void Create_ReturnsCachedProvider_ForOAuthPkce()
    {
        // Arrange
        var options = new BricksNetClientOptions
        {
            Host = "host.example.com",
            HttpPath = "/sql/1.0/warehouses/abc",
            AuthType = AuthType.OAuthPkce,
        };
        using var httpClient = new HttpClient();

        // Act
        var provider = AuthProviderFactory.Create(
            options,
            oauthOptions: CreateOAuthOptions(),
            tokenExchangeClient: httpClient);

        // Assert
        Assert.IsType<CachedTokenProvider>(provider);
    }

    [Fact]
    public void Create_Throws_WhenOAuthOptionsMissing()
    {
        // Arrange
        var options = new BricksNetClientOptions
        {
            Host = "host.example.com",
            HttpPath = "/sql/1.0/warehouses/abc",
            AuthType = AuthType.OAuthPkce,
        };
        using var httpClient = new HttpClient();

        // Act / Assert
        Assert.Throws<ArgumentNullException>(() =>
            AuthProviderFactory.Create(options, oauthOptions: null, tokenExchangeClient: httpClient));
    }

    [Fact]
    public void Create_Throws_WhenTokenExchangeClientMissing()
    {
        // Arrange
        var options = new BricksNetClientOptions
        {
            Host = "host.example.com",
            HttpPath = "/sql/1.0/warehouses/abc",
            AuthType = AuthType.OAuthPkce,
        };

        // Act / Assert
        Assert.Throws<ArgumentNullException>(() =>
            AuthProviderFactory.Create(options, oauthOptions: CreateOAuthOptions(), tokenExchangeClient: null));
    }
}
