using BricksNet.Contracts;

namespace BricksNet.Auth;

/// <summary>
/// <see cref="IAuthProvider"/> backed by a static Databricks Personal Access Token (PAT). The token
/// is returned unchanged on every request.
/// </summary>
/// <remarks>
/// The token is a credential and is never written to logs. The value is validated to contain no
/// CR/LF characters to prevent HTTP header injection when it is later set on the
/// <c>Authorization</c> header.
/// </remarks>
public sealed class PatAuthProvider : IAuthProvider
{
    private readonly string _token;

    /// <summary>Creates a provider that always returns <paramref name="token"/>.</summary>
    /// <param name="token">The personal access token. Must be non-empty and contain no CR/LF.</param>
    public PatAuthProvider(string token)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);
        if (token.AsSpan().IndexOfAny('\r', '\n') >= 0)
        {
            // Prevent HTTP header injection (CR/LF) via a malformed credential.
            throw new ArgumentException("Token contains CR/LF characters.", nameof(token));
        }

        _token = token;
    }

    /// <inheritdoc />
    public Task<string> GetTokenAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_token);
}
