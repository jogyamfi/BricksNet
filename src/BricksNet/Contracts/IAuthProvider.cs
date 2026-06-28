namespace BricksNet.Contracts;

/// <summary>Provides a bearer token for outbound HTTP requests.</summary>
public interface IAuthProvider
{
    /// <summary>Returns a valid bearer token, refreshing it if necessary.</summary>
    Task<string> GetTokenAsync(CancellationToken cancellationToken = default);
}
