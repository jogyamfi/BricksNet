namespace BricksNet.Auth;

/// <summary>
/// Pluggable persistence for OAuth tokens, used by <see cref="OAuthTokenCache"/> to survive process
/// restarts. Implementations <b>must</b> encrypt tokens at rest — never store them in plaintext.
/// On Windows, prefer <c>ProtectedData.Protect</c> (DPAPI); in ASP.NET Core hosts, prefer
/// <c>IDataProtector</c>.
/// </summary>
public interface IOAuthPersistence
{
    /// <summary>Stores (or overwrites) the token associated with <paramref name="key"/>.</summary>
    Task StoreAsync(string key, PersistedToken token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the token associated with <paramref name="key"/>, or <see langword="null"/> when none
    /// is persisted.
    /// </summary>
    Task<PersistedToken?> LoadAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Removes any token associated with <paramref name="key"/>.</summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}

/// <summary>An access token plus its expiry, as stored by an <see cref="IOAuthPersistence"/>.</summary>
public sealed record PersistedToken
{
    /// <summary>The bearer access token. Sensitive — must be encrypted at rest.</summary>
    public required string AccessToken { get; init; }

    /// <summary>Optional refresh token. Sensitive — must be encrypted at rest.</summary>
    public string? RefreshToken { get; init; }

    /// <summary>Absolute UTC instant at which <see cref="AccessToken"/> expires.</summary>
    public DateTimeOffset ExpiresAt { get; init; }
}
