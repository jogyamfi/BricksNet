using BricksNet.AdoNet;

namespace BricksNet.Backends;

/// <summary>
/// Implemented by operation backends that retain the server-reported error details once a
/// status poll has observed a failed state, so callers can surface the real message,
/// SQLSTATE and error code instead of a generic failure.
/// </summary>
internal interface IOperationFailureSource
{
    /// <summary>
    /// Builds an exception describing the most recently observed failure, or returns
    /// <see langword="null"/> when no failure details are available.
    /// </summary>
    BricksNetException? CreateFailureException();
}
