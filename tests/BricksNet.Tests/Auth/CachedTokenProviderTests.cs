using System.Threading.Tasks;
using BricksNet.Auth;
using BricksNet.Contracts;
using Xunit;

namespace BricksNet.Auth;

public class CachedTokenProviderTests
{
    [Fact]
    public async Task GetTokenAsync_ReturnsCachedToken_WithoutRefreshing_WhenFarFromExpiry()
    {
        // Arrange
        var inner = new FakeExpiringProvider(expiresIn: TimeSpan.FromMinutes(10));
        var provider = new CachedTokenProvider(inner);

        // Act
        string first = await provider.GetTokenAsync();
        string second = await provider.GetTokenAsync();

        // Assert
        Assert.Equal(first, second);
        Assert.Equal(1, inner.CallCount);
    }

    [Fact]
    public async Task GetTokenAsync_Refreshes_WhenRemainingLifetimeBelowBuffer()
    {
        // Arrange — token expires within the refresh buffer, so each call must re-acquire.
        var inner = new FakeExpiringProvider(expiresIn: TimeSpan.FromSeconds(5));
        var provider = new CachedTokenProvider(inner);

        // Act
        await provider.GetTokenAsync();
        await provider.GetTokenAsync();

        // Assert
        Assert.Equal(2, inner.CallCount);
    }

    [Fact]
    public async Task GetTokenAsync_DelegatesEveryCall_WhenInnerCannotReportExpiry()
    {
        // Arrange
        var inner = new FakePlainProvider();
        var provider = new CachedTokenProvider(inner);

        // Act
        await provider.GetTokenAsync();
        await provider.GetTokenAsync();

        // Assert
        Assert.Equal(2, inner.CallCount);
    }

    private sealed class FakeExpiringProvider : IAuthProvider, ISupportsTokenExpiry
    {
        private readonly TimeSpan _expiresIn;

        public FakeExpiringProvider(TimeSpan expiresIn) => _expiresIn = expiresIn;

        public int CallCount { get; private set; }

        public Task<string> GetTokenAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult($"token-{++CallCount}");

        public Task<AccessToken> GetTokenWithExpiryAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new AccessToken($"token-{CallCount}", DateTimeOffset.UtcNow + _expiresIn));
        }
    }

    private sealed class FakePlainProvider : IAuthProvider
    {
        public int CallCount { get; private set; }

        public Task<string> GetTokenAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult($"token-{++CallCount}");
    }
}
