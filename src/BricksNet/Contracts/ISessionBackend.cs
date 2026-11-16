using BricksNet.Contracts.Options;

namespace BricksNet.Contracts;

/// <summary>
/// Backend handle scoped to a single session. Returned by <see cref="IBackend.OpenSessionAsync"/>.
/// Mirrors <c>ISessionBackend</c> from <c>databricks-sql-nodejs/lib/contracts/ISessionBackend.ts</c>.
/// </summary>
internal interface ISessionBackend
{
    /// <summary>Opaque session identifier assigned by the server.</summary>
    string Id { get; }

    /// <summary>Executes a SQL statement and returns an operation handle.</summary>
    Task<IOperationBackend> ExecuteStatementAsync(
        string sql,
        StatementOptions? options,
        CancellationToken cancellationToken = default);

    /// <summary>Lists all accessible catalogs.</summary>
    Task<IOperationBackend> GetCatalogsAsync(CancellationToken cancellationToken = default);

    /// <summary>Lists schemas, optionally filtered by catalog and name pattern.</summary>
    Task<IOperationBackend> GetSchemasAsync(
        string? catalogName,
        string? schemaNamePattern,
        CancellationToken cancellationToken = default);

    /// <summary>Lists tables, optionally filtered by catalog, schema, and name pattern.</summary>
    Task<IOperationBackend> GetTablesAsync(
        string? catalogName,
        string? schemaNamePattern,
        string? tableNamePattern,
        CancellationToken cancellationToken = default);

    /// <summary>Lists columns, optionally filtered by catalog, schema, table, and column name pattern.</summary>
    Task<IOperationBackend> GetColumnsAsync(
        string? catalogName,
        string? schemaNamePattern,
        string? tableNamePattern,
        string? columnNamePattern,
        CancellationToken cancellationToken = default);

    /// <summary>Closes the session. Idempotent.</summary>
    Task CloseSessionAsync(CancellationToken cancellationToken = default);
}
