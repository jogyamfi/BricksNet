using System.Data.Common;

namespace BricksNet.AdoNet;

/// <summary>
/// The exception type thrown for Databricks SQL errors surfaced through BricksNet.
/// Carries the SQL state, the originating HTTP/Thrift error code, and (when known)
/// the server-side query identifier.
/// </summary>
public class BricksNetException : DbException
{
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

    /// <summary>Initialises a new instance with full Databricks error context.</summary>
    public BricksNetException(string message, string? sqlState, int errorCode, string? queryId)
        : base(message)
    {
        SqlState = sqlState;
        _errorCode = errorCode;
        QueryId = queryId;
    }

    /// <summary>The SQLSTATE returned by the server, when available.</summary>
    public override string? SqlState { get; }

    /// <summary>The HTTP status or Thrift status code associated with the error.</summary>
    public override int ErrorCode => _errorCode;

    /// <summary>The server-side query/statement identifier, when available.</summary>
    public string? QueryId { get; }

    private readonly int _errorCode;
}
