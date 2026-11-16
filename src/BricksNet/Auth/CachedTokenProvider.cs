using BricksNet.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BricksNet.Auth;

/// <summary>An access token together with its absolute UTC expiry.</summary>
internal readonly record struct AccessToken(string Value, DateTimeOffset ExpiresAtUtc);

/// <summary>
/// Implemented by auth providers that can report the expiry of the token they issue, enabling
/// <see cref="CachedTokenProvider"/> to refresh proactively before expiry.
/// </summary>
internal interface ISupportsTokenExpiry
{
    Task<AccessToken> GetTokenWithExpiryAsync(CancellationToken cancellationToken);
}

/// <summary>
/// <see cref="IAuthProvider"/> decorator that caches the inner provider's token in memory and
/// refreshes it proactively when its remaining lifetime falls below <see cref="RefreshBuffer"/>.
/// </summary>
/// <remarks>
/// Concurrent callers are serialised through a single-slot gate so only one refresh runs at a time.
/// When the inner provider cannot report an expiry, every call delegates straight through without
/// caching. Token values are credentials and are never logged.
/// </remarks>
internal sealed class CachedTokenProvider : IAuthProvider
{
    /// <summary>Remaining lifetime below which a cached token is refreshed eagerly.</summary>
    public static readonly TimeSpan RefreshBuffer = TimeSpan.FromSeconds(30);

    private readonly IAuthProvider _inner;
    private readonly ILogger<CachedTokenProvider> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private AccessToken? _cached;

    /// <summary>Wraps <paramref name="inner"/> with proactive, in-memory token caching.</summary>
    public CachedTokenProvider(IAuthProvider inner, ILogger<CachedTokenProvider>? logger = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _logger = logger ?? NullLogger<CachedTokenProvider>.Instance;
    }

    /// <inheritdoc />
    public async Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        // When the inner provider can't report expiry, there is nothing useful to cache.
        if (_inner is not ISupportsTokenExpiry expiringProvider)
        {
            return await _inner.GetTokenAsync(cancellationToken).ConfigureAwait(false);
        }

        if (TryGetFreshCachedToken(out string? cached))
        {
            return cached;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Re-check after acquiring the gate: another caller may have refreshed.
            if (TryGetFreshCachedToken(out cached))
            {
                return cached;
            }

            _logger.LogDebug("Refreshing OAuth token via inner provider.");
            AccessToken token = await expiringProvider.GetTokenWithExpiryAsync(cancellationToken).ConfigureAwait(false);
            _cached = token;
            return token.Value;
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool TryGetFreshCachedToken(out string token)
    {
        if (_cached is { } current && current.ExpiresAtUtc - DateTimeOffset.UtcNow > RefreshBuffer)
        {
            token = current.Value;
            return true;
        }

        token = string.Empty;
        return false;
    }
}
