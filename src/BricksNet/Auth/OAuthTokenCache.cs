using BricksNet.Contracts;

namespace BricksNet.Auth;

/// <summary>
/// Thread-safe in-memory <see cref="ITokenCache"/> with least-recently-used (LRU) eviction and
/// per-entry time-to-live. An optional <see cref="IOAuthPersistence"/> provides durable, encrypted
/// storage that survives process restarts.
/// </summary>
/// <remarks>
/// Expired entries are treated as cache misses and removed lazily on access. Token values are
/// credentials and are never logged.
/// </remarks>
internal sealed class OAuthTokenCache : ITokenCache
{
    private const int DefaultCapacity = 32;

    private readonly int _capacity;
    private readonly IOAuthPersistence? _persistence;
    private readonly object _gate = new();

    // LRU bookkeeping: most-recently-used at the head of the list.
    private readonly LinkedList<Entry> _lruList = new();
    private readonly Dictionary<string, LinkedListNode<Entry>> _index = new(StringComparer.Ordinal);

    /// <summary>Creates an in-memory cache with the given <paramref name="capacity"/>.</summary>
    /// <param name="capacity">Maximum number of entries before LRU eviction. Must be positive.</param>
    /// <param name="persistence">Optional durable, encrypted persistence layer.</param>
    public OAuthTokenCache(int capacity = DefaultCapacity, IOAuthPersistence? persistence = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _capacity = capacity;
        _persistence = persistence;
    }

    /// <inheritdoc />
    public async Task<string?> TryGetTokenAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        DateTimeOffset now = DateTimeOffset.UtcNow;

        lock (_gate)
        {
            if (_index.TryGetValue(key, out LinkedListNode<Entry>? node))
            {
                if (node.Value.ExpiresAt > now)
                {
                    // Refresh recency: move to the head of the LRU list.
                    _lruList.Remove(node);
                    _lruList.AddFirst(node);
                    return node.Value.Token;
                }

                // Expired — drop it.
                _lruList.Remove(node);
                _index.Remove(key);
            }
        }

        // Fall back to durable storage, if configured.
        if (_persistence is not null)
        {
            PersistedToken? persisted = await _persistence.LoadAsync(key, cancellationToken).ConfigureAwait(false);
            if (persisted is not null && persisted.ExpiresAt > now)
            {
                StoreInMemory(key, persisted.AccessToken, persisted.ExpiresAt);
                return persisted.AccessToken;
            }
        }

        return null;
    }

    /// <inheritdoc />
    public async Task StoreTokenAsync(
        string key,
        string token,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentException.ThrowIfNullOrEmpty(token);

        StoreInMemory(key, token, expiresAt);

        if (_persistence is not null)
        {
            var persisted = new PersistedToken { AccessToken = token, ExpiresAt = expiresAt };
            await _persistence.StoreAsync(key, persisted, cancellationToken).ConfigureAwait(false);
        }
    }

    private void StoreInMemory(string key, string token, DateTimeOffset expiresAt)
    {
        lock (_gate)
        {
            if (_index.TryGetValue(key, out LinkedListNode<Entry>? existing))
            {
                existing.Value = new Entry(key, token, expiresAt);
                _lruList.Remove(existing);
                _lruList.AddFirst(existing);
                return;
            }

            var node = new LinkedListNode<Entry>(new Entry(key, token, expiresAt));
            _lruList.AddFirst(node);
            _index[key] = node;

            if (_index.Count > _capacity)
            {
                LinkedListNode<Entry>? lru = _lruList.Last;
                if (lru is not null)
                {
                    _lruList.RemoveLast();
                    _index.Remove(lru.Value.Key);
                }
            }
        }
    }

    private readonly record struct Entry(string Key, string Token, DateTimeOffset ExpiresAt);
}
