using Microsoft.Extensions.Logging;

namespace BricksNet.Telemetry;

/// <summary>
/// Source-generated, allocation-free structured log messages for BricksNet.
/// </summary>
/// <remarks>
/// <para>
/// Each event uses the <see cref="LoggerMessageAttribute"/> source generator, which avoids boxing
/// and string formatting when the target level is disabled. Never pass interpolated strings to
/// <c>ILogger</c> directly — route every event through a method here.
/// </para>
/// <para>Level guidelines:</para>
/// <list type="bullet">
///   <item><description><see cref="LogLevel.Trace"/> — per-row / per-chunk detail.</description></item>
///   <item><description><see cref="LogLevel.Debug"/> — per-statement lifecycle and HTTP retries.</description></item>
///   <item><description><see cref="LogLevel.Information"/> — connection / session lifecycle.</description></item>
///   <item><description><see cref="LogLevel.Warning"/> — retries exhausted but recovered; deprecated options.</description></item>
///   <item><description><see cref="LogLevel.Error"/> — unrecoverable failures.</description></item>
/// </list>
/// <para>
/// Security: never log token or credential values, and never log full SQL text at
/// <see cref="LogLevel.Information"/> or above.
/// </para>
/// </remarks>
public static partial class LogMessages
{
    // ---- Connection / session lifecycle (Information) — EventId 1000–1099 --------------------------

    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Information,
        Message = "Opening connection to {ServerAddress}:{ServerPort} using {Backend} backend.")]
    public static partial void ConnectionOpening(ILogger logger, string serverAddress, int serverPort, string backend);

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "Connection opened to {ServerAddress}:{ServerPort}.")]
    public static partial void ConnectionOpened(ILogger logger, string serverAddress, int serverPort);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Information,
        Message = "Connection closed.")]
    public static partial void ConnectionClosed(ILogger logger);

    [LoggerMessage(
        EventId = 1010,
        Level = LogLevel.Information,
        Message = "Session {SessionId} opened.")]
    public static partial void SessionOpened(ILogger logger, string sessionId);

    [LoggerMessage(
        EventId = 1011,
        Level = LogLevel.Information,
        Message = "Session {SessionId} closed.")]
    public static partial void SessionClosed(ILogger logger, string sessionId);

    // ---- Statement lifecycle (Debug / Error) — EventId 1100–1199 -----------------------------------

    [LoggerMessage(
        EventId = 1100,
        Level = LogLevel.Debug,
        Message = "Executing statement ({SqlLength} chars) on {Backend} backend.")]
    public static partial void StatementExecuting(ILogger logger, int sqlLength, string backend);

    [LoggerMessage(
        EventId = 1101,
        Level = LogLevel.Debug,
        Message = "Statement {StatementId} succeeded.")]
    public static partial void StatementSucceeded(ILogger logger, string statementId);

    [LoggerMessage(
        EventId = 1102,
        Level = LogLevel.Debug,
        Message = "Statement {StatementId} cancelled.")]
    public static partial void StatementCancelled(ILogger logger, string statementId);

    [LoggerMessage(
        EventId = 1103,
        Level = LogLevel.Error,
        Message = "Statement {StatementId} failed: {ErrorCode} {SqlState}.")]
    public static partial void StatementFailed(ILogger logger, string statementId, string errorCode, string sqlState);

    // ---- Result fetch (Trace) — EventId 1200–1299 -------------------------------------------------

    [LoggerMessage(
        EventId = 1200,
        Level = LogLevel.Trace,
        Message = "Fetched result chunk {ChunkIndex} with {RowCount} rows for statement {StatementId}.")]
    public static partial void ResultChunkFetched(ILogger logger, int chunkIndex, int rowCount, string statementId);

    // ---- HTTP retries (Debug / Warning) — EventId 1300–1399 ---------------------------------------

    [LoggerMessage(
        EventId = 1300,
        Level = LogLevel.Debug,
        Message = "Retrying request (attempt {Attempt}) after {DelayMs} ms due to status {StatusCode}.")]
    public static partial void RetryingRequest(ILogger logger, int attempt, double delayMs, int statusCode);

    [LoggerMessage(
        EventId = 1301,
        Level = LogLevel.Warning,
        Message = "Request succeeded after {Attempts} retries.")]
    public static partial void RetriesRecovered(ILogger logger, int attempts);

    [LoggerMessage(
        EventId = 1302,
        Level = LogLevel.Warning,
        Message = "Retries exhausted after {Attempts} attempts; last status {StatusCode}.")]
    public static partial void RetriesExhausted(ILogger logger, int attempts, int statusCode);

    // ---- Authentication (Debug) — EventId 1400–1499 -----------------------------------------------

    [LoggerMessage(
        EventId = 1400,
        Level = LogLevel.Debug,
        Message = "Acquiring authentication token using {AuthType}.")]
    public static partial void AuthTokenAcquiring(ILogger logger, string authType);

    [LoggerMessage(
        EventId = 1401,
        Level = LogLevel.Debug,
        Message = "Authentication token acquired using {AuthType}.")]
    public static partial void AuthTokenAcquired(ILogger logger, string authType);

    // ---- Configuration (Warning) — EventId 1500–1599 ----------------------------------------------

    [LoggerMessage(
        EventId = 1500,
        Level = LogLevel.Warning,
        Message = "Option '{OptionName}' is deprecated: {Reason}")]
    public static partial void DeprecatedOption(ILogger logger, string optionName, string reason);
}
