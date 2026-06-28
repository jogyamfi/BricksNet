using System.Threading.Tasks;
using BricksNet.Auth;
using Moq;
using Xunit;

namespace BricksNet.Auth;

public class OAuthTokenCacheTests
{
    private static readonly DateTimeOffset FarFuture = DateTimeOffset.UtcNow.AddHours(1);

    [Fact]
    public async Task TryGetTokenAsync_ReturnsStoredToken_WhenNotExpired()
    {
        // Arrange
        var cache = new OAuthTokenCache();
        await cache.StoreTokenAsync("key", "token-1", FarFuture);

        // Act
        string? token = await cache.TryGetTokenAsync("key");

        // Assert
        Assert.Equal("token-1", token);
    }

    [Fact]
    public async Task TryGetTokenAsync_ReturnsNull_WhenTokenExpired()
    {
        // Arrange
        var cache = new OAuthTokenCache();
        await cache.StoreTokenAsync("key", "token-1", DateTimeOffset.UtcNow.AddSeconds(-1));

        // Act
        string? token = await cache.TryGetTokenAsync("key");

        // Assert
        Assert.Null(token);
    }

    [Fact]
    public async Task TryGetTokenAsync_ReturnsNull_WhenKeyMissing()
    {
        // Arrange
        var cache = new OAuthTokenCache();

        // Act
        string? token = await cache.TryGetTokenAsync("absent");

        // Assert
        Assert.Null(token);
    }

    [Fact]
    public async Task StoreTokenAsync_EvictsLeastRecentlyUsed_WhenCapacityExceeded()
    {
        // Arrange
        var cache = new OAuthTokenCache(capacity: 2);
        await cache.StoreTokenAsync("a", "ta", FarFuture);
        await cache.StoreTokenAsync("b", "tb", FarFuture);

        // Touch "a" so "b" becomes least-recently-used.
        await cache.TryGetTokenAsync("a");

        // Act — inserting "c" should evict "b".
        await cache.StoreTokenAsync("c", "tc", FarFuture);

        // Assert
        Assert.Equal("ta", await cache.TryGetTokenAsync("a"));
        Assert.Null(await cache.TryGetTokenAsync("b"));
        Assert.Equal("tc", await cache.TryGetTokenAsync("c"));
    }

    [Fact]
    public async Task TryGetTokenAsync_FallsBackToPersistence_WhenNotInMemory()
    {
        // Arrange
        var persistence = new Mock<IOAuthPersistence>();
        persistence
            .Setup(p => p.LoadAsync("key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PersistedToken { AccessToken = "persisted", ExpiresAt = FarFuture });

        var cache = new OAuthTokenCache(persistence: persistence.Object);

        // Act
        string? token = await cache.TryGetTokenAsync("key");

        // Assert
        Assert.Equal("persisted", token);
        persistence.Verify(p => p.LoadAsync("key", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StoreTokenAsync_WritesThroughToPersistence()
    {
        // Arrange
        var persistence = new Mock<IOAuthPersistence>();
        var cache = new OAuthTokenCache(persistence: persistence.Object);

        // Act
        await cache.StoreTokenAsync("key", "token-1", FarFuture);

        // Assert
        persistence.Verify(
            p => p.StoreAsync(
                "key",
                It.Is<PersistedToken>(t => t.AccessToken == "token-1" && t.ExpiresAt == FarFuture),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
