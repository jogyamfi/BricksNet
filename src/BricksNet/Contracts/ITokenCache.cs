namespace BricksNet.Contracts;

/// <summary>Persistent token cache for OAuth tokens.</summary>
public interface ITokenCache
{
    /// <summary>
    /// Attempts to retrieve a cached, non-expired token for <paramref name="key"/>.
    /// Returns <see langword="null"/> when no valid token is cached.
    /// </summary>
    Task<string?> TryGetTokenAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Stores <paramref name="token"/> in the cache, expiring at <paramref name="expiresAt"/>.</summary>
    Task StoreTokenAsync(
        string key,
        string token,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default);
}
