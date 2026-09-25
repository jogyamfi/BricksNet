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
    /// <remarks>
    /// The result shape depends on the backend. Thrift returns JDBC-style columns
    /// (<c>TABLE_CAT</c>, ...). REST has no metadata RPCs and runs <c>SHOW CATALOGS</c>, whose
    /// columns follow Databricks SQL (<c>catalog</c>).
    /// </remarks>
    Task<IBricksNetOperation> GetCatalogsAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns an operation whose results enumerate schemas matching the supplied filters.</summary>
    /// <remarks>
    /// Thrift returns JDBC-style columns (<c>TABLE_SCHEM</c>, <c>TABLE_CATALOG</c>). REST runs
    /// <c>SHOW SCHEMAS [IN catalog] [LIKE pattern]</c>, which returns <c>databaseName</c>. <c>%</c> in
    /// the pattern is translated to the Databricks <c>*</c> wildcard; <c>_</c> has no equivalent.
    /// </remarks>
    Task<IBricksNetOperation> GetSchemasAsync(
        string? catalogName = null,
        string? schemaNamePattern = null,
        CancellationToken cancellationToken = default);

    /// <summary>Returns an operation whose results enumerate tables matching the supplied filters.</summary>
    /// <remarks>
    /// Thrift returns JDBC-style columns (<c>TABLE_CAT</c>, <c>TABLE_SCHEM</c>, <c>TABLE_NAME</c>, ...).
    /// REST runs <c>SHOW TABLES [IN catalog.schema] [LIKE pattern]</c>, which returns
    /// <c>database</c>, <c>tableName</c> and <c>isTemporary</c>; the schema argument is treated as a
    /// literal name, not a pattern, and <c>%</c> in the table pattern becomes <c>*</c>.
    /// </remarks>
    Task<IBricksNetOperation> GetTablesAsync(
        string? catalogName = null,
        string? schemaNamePattern = null,
        string? tableNamePattern = null,
        CancellationToken cancellationToken = default);

    /// <summary>Returns an operation whose results enumerate columns matching the supplied filters.</summary>
    /// <remarks>
    /// Thrift returns JDBC-style columns and supports patterns for every argument. REST runs
    /// <c>SHOW COLUMNS IN catalog.schema.table</c>: a table name is required (otherwise
    /// <see cref="ArgumentException"/> is thrown), names are literal rather than patterns, and
    /// <paramref name="columnNamePattern"/> is ignored — filter the returned <c>col_name</c> rows instead.
    /// </remarks>
    Task<IBricksNetOperation> GetColumnsAsync(
        string? catalogName = null,
        string? schemaNamePattern = null,
        string? tableNamePattern = null,
        string? columnNamePattern = null,
        CancellationToken cancellationToken = default);

    /// <summary>Closes the session. Idempotent.</summary>
    Task CloseAsync(CancellationToken cancellationToken = default);
}
