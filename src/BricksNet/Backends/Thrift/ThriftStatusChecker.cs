using BricksNet.AdoNet;
using BricksNet.Backends.Thrift.Generated;

namespace BricksNet.Backends.Thrift;

/// <summary>
/// Validates the <see cref="TStatus"/> carried by every TCLIService response and
/// translates failures into <see cref="BricksNetException"/>.
/// </summary>
internal static class ThriftStatusChecker
{
    /// <summary>
    /// Throws a <see cref="BricksNetException"/> when <paramref name="status"/> reports
    /// an error or an unexpected still-executing state. Success and
    /// success-with-info statuses return normally.
    /// </summary>
    /// <param name="status">The status reported by the server.</param>
    /// <param name="queryId">The associated query identifier, when known.</param>
    public static void EnsureSuccess(TStatus? status, string? queryId = null)
    {
        if (status is null)
        {
            throw new BricksNetException("The Databricks service returned a response without a status.");
        }

        switch (status.StatusCode)
        {
            case TStatusCode.SuccessStatus:
            case TStatusCode.SuccessWithInfoStatus:
                return;

            case TStatusCode.StillExecutingStatus:
                throw new BricksNetException(
                    BuildMessage(status, "The operation is still executing."),
                    status.SqlState,
                    status.ErrorCode ?? 0,
                    queryId);

            case TStatusCode.ErrorStatus:
            case TStatusCode.InvalidHandleStatus:
            default:
                throw new BricksNetException(
                    BuildMessage(status, "The Databricks service reported an error."),
                    status.SqlState,
                    status.ErrorCode ?? 0,
                    queryId);
        }
    }

    private static string BuildMessage(TStatus status, string fallback)
    {
        var message = status.DisplayMessage;
        if (string.IsNullOrWhiteSpace(message))
        {
            message = status.ErrorMessage;
        }

        return string.IsNullOrWhiteSpace(message) ? fallback : message!;
    }
}
