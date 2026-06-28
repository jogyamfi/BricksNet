using System.Threading.Tasks;
using BricksNet.Auth;
using Xunit;

namespace BricksNet.Auth;

public class PatAuthProviderTests
{
    [Fact]
    public async Task GetTokenAsync_ReturnsConfiguredToken()
    {
        // Arrange
        var provider = new PatAuthProvider("dapi-secret-token");

        // Act
        string token = await provider.GetTokenAsync();

        // Assert
        Assert.Equal("dapi-secret-token", token);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Constructor_Throws_WhenTokenNullOrEmpty(string? token)
    {
        // Act / Assert
        Assert.ThrowsAny<ArgumentException>(() => new PatAuthProvider(token!));
    }

    [Theory]
    [InlineData("token\rwith-cr")]
    [InlineData("token\nwith-lf")]
    [InlineData("token\r\nwith-crlf")]
    public void Constructor_Throws_WhenTokenContainsCrLf(string token)
    {
        // Act / Assert
        ArgumentException ex = Assert.Throws<ArgumentException>(() => new PatAuthProvider(token));
        Assert.Equal("token", ex.ParamName);
    }
}
