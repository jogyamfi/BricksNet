using System.Net.Http.Headers;
using BricksNet.Contracts;
using Microsoft.Extensions.Logging;

namespace BricksNet.Connection.Handlers;

/// <summary>
/// Delegating handler that obtains a bearer token from an <see cref="IAuthProvider"/>
/// and attaches it to each outbound request as an <c>Authorization: Bearer</c> header.
/// </summary>
/// <remarks>
/// Placed inside the retry handler in the pipeline so the token is re-fetched on every
/// retry attempt — a freshly-cached or refreshed token is used when an attempt is replayed.
/// The token value is never written to logs.
/// </remarks>
internal sealed class AuthHeaderHandler : DelegatingHandler
{
    private readonly IAuthProvider _authProvider;
    private readonly ILogger<AuthHeaderHandler> _logger;

    public AuthHeaderHandler(IAuthProvider authProvider, ILogger<AuthHeaderHandler> logger)
    {
        _authProvider = authProvider ?? throw new ArgumentNullException(nameof(authProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string token = await _authProvider.GetTokenAsync(cancellationToken).ConfigureAwait(false);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        _logger.LogDebug("Attached bearer token to {Method} {Uri}.", request.Method, request.RequestUri);

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
