using System.Data.Common;
using System.Text.RegularExpressions;

namespace BricksNet.AdoNet;

/// <summary>
/// The exception type thrown for Databricks SQL errors surfaced through BricksNet.
/// Carries the SQL state, the originating HTTP/Thrift error code, the Databricks error class
/// and (when known) the server-side query identifier.
/// </summary>
public partial class BricksNetException : DbException
{
    /// <summary>
    /// Error classes raised when a concurrent Delta transaction conflicts with this one. The
    /// statement did not commit and can safely be retried.
    /// </summary>
    private static readonly HashSet<string> TransientErrorClasses = new(StringComparer.Ordinal)
    {
        "DELTA_CONCURRENT_APPEND",
        "DELTA_CONCURRENT_DELETE_DELETE",
        "DELTA_CONCURRENT_DELETE_READ",
        "DELTA_CONCURRENT_TRANSACTION",
        "DELTA_CONCURRENT_WRITE",
        "DELTA_METADATA_CHANGED",
        "DELTA_PROTOCOL_CHANGED",
    };

    private readonly int _errorCode;

    /// <summary>Initialises a new instance with a message.</summary>
    public BricksNetException(string message)
        : base(message)
    {
    }

    /// <summary>Initialises a new instance with a message and inner exception.</summary>
    public BricksNetException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initialises a new instance with full Databricks error context. The error class is parsed
    /// from the <c>[ERROR_CLASS]</c> marker in <paramref name="message"/>, when present.
    /// </summary>
    public BricksNetException(string message, string? sqlState, int errorCode, string? queryId)
        : this(message, sqlState, errorCode, queryId, ParseErrorClass(message))
    {
    }

    /// <summary>Initialises a new instance with full Databricks error context and an explicit error class.</summary>
    public BricksNetException(string message, string? sqlState, int errorCode, string? queryId, string? errorClass)
        : base(message)
    {
        SqlState = sqlState;
        _errorCode = errorCode;
        QueryId = queryId;
        ErrorClass = errorClass;
    }

    /// <summary>The SQLSTATE returned by the server, when available.</summary>
    public override string? SqlState { get; }

    /// <summary>The HTTP status or Thrift status code associated with the error.</summary>
    public override int ErrorCode => _errorCode;

    /// <summary>The server-side query/statement identifier, when available.</summary>
    public string? QueryId { get; }

    /// <summary>
    /// The Databricks error class (for example <c>DELTA_CONCURRENT_APPEND</c> or
    /// <c>UNRESOLVED_COLUMN.WITH_SUGGESTION</c>), when the server reported one.
    /// </summary>
    public string? ErrorClass { get; }

    /// <summary>
    /// <see langword="true"/> when retrying may succeed: the service was throttled or unavailable
    /// (HTTP 429/503), or a concurrent Delta transaction conflicted with the statement.
    /// </summary>
    public override bool IsTransient =>
        _errorCode is 429 or 503
        || (ErrorClass is { } errorClass && TransientErrorClasses.Contains(MainClass(errorClass)));

    /// <summary>
    /// Extracts the first <c>[ERROR_CLASS]</c> or <c>[ERROR_CLASS.SUB_CLASS]</c> marker from a
    /// Databricks error message, or returns <see langword="null"/> when there is none.
    /// </summary>
    internal static string? ParseErrorClass(string? message) =>
        message is not null && ErrorClassRegex().Match(message) is { Success: true } match
            ? match.Groups[1].Value
            : null;

    private static string MainClass(string errorClass)
    {
        var dot = errorClass.IndexOf('.', StringComparison.Ordinal);
        return dot < 0 ? errorClass : errorClass[..dot];
    }

    [GeneratedRegex(@"\[([A-Z][A-Z0-9]*(?:_[A-Z0-9]+)+(?:\.[A-Z0-9_]+)*)\]", RegexOptions.CultureInvariant)]
    private static partial Regex ErrorClassRegex();
}
