using Microsoft.AspNetCore.Http;

namespace BricksNet.IntegrationTests.FakeService;

/// <summary>
/// Handles an incoming request against the in-process fake service. Implemented by the
/// replayer (offline fixtures) and the recorder (live forwarding + capture).
/// </summary>
internal interface IFakeResponder
{
    /// <summary>
    /// Attempts to produce a response for <paramref name="context"/>.
    /// </summary>
    /// <returns><see langword="true"/> if the request was handled; otherwise <see langword="false"/>.</returns>
    Task<bool> TryRespondAsync(HttpContext context);
}
