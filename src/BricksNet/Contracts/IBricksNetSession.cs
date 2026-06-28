using BricksNet.Contracts.Options;

namespace BricksNet.Contracts;

/// <summary>
/// Represents an open session on a Databricks SQL warehouse or cluster.
/// Mirrors <c>DBSQLSession</c> from <c>databricks-sql-nodejs/lib/DBSQLSession.ts</c>.
/// </summary>
public interface IBricksNetSession : IAsyncDisposable
{
    /// <summary>Executes a SQL statement and returns an operation handle.</summary>
    Task<IBricksNetOperation> ExecuteStatementAsync(
        string sql,
        StatementOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>Returns an operation whose results enumerate accessible catalogs.</summary>
    Task<IBricksNetOperation> GetCatalogsAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns an operation whose results enumerate schemas matching the supplied filters.</summary>
    Task<IBricksNetOperation> GetSchemasAsync(
        string? catalogName = null,
        string? schemaNamePattern = null,
        CancellationToken cancellationToken = default);

    /// <summary>Returns an operation whose results enumerate tables matching the supplied filters.</summary>
    Task<IBricksNetOperation> GetTablesAsync(
        string? catalogName = null,
        string? schemaNamePattern = null,
        string? tableNamePattern = null,
        CancellationToken cancellationToken = default);

    /// <summary>Returns an operation whose results enumerate columns matching the supplied filters.</summary>
    Task<IBricksNetOperation> GetColumnsAsync(
        string? catalogName = null,
        string? schemaNamePattern = null,
        string? tableNamePattern = null,
        string? columnNamePattern = null,
        CancellationToken cancellationToken = default);

    /// <summary>Closes the session. Idempotent.</summary>
    Task CloseAsync(CancellationToken cancellationToken = default);
}
