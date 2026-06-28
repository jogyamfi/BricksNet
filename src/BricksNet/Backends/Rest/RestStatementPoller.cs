using BricksNet.Backends.Rest.Dto;

namespace BricksNet.Backends.Rest;

/// <summary>
/// Polls <c>GET /api/2.0/sql/statements/{id}</c> until the statement reaches a terminal state.
/// Uses exponential backoff starting at 200 ms and capped at 10 s, and honours the supplied
/// <see cref="CancellationToken"/>.
/// </summary>
internal sealed class RestStatementPoller
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(10);

    private readonly SqlExecHttpClient _client;

    public RestStatementPoller(SqlExecHttpClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <summary>
    /// Polls the statement until it reaches a terminal state and returns the final response.
    /// </summary>
    /// <param name="statementId">The statement identifier to poll.</param>
    /// <param name="cancellationToken">Token used to stop polling.</param>
    /// <exception cref="OperationCanceledException">Thrown when cancellation is requested.</exception>
    public async Task<ExecuteStatementResponse> PollUntilTerminalAsync(
        string statementId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(statementId);

        var delay = InitialDelay;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var response = await _client
                .GetStatementAsync(statementId, cancellationToken)
                .ConfigureAwait(false);

            if (IsTerminal(response.Status.State))
            {
                return response;
            }

            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            delay = delay < MaxDelay
                ? TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxDelay.Ticks))
                : MaxDelay;
        }
    }

    /// <summary>Returns <see langword="true"/> when the state will not change further.</summary>
    public static bool IsTerminal(StatementState state) => state is
        StatementState.Succeeded or
        StatementState.Failed or
        StatementState.Canceled or
        StatementState.Closed;
}
