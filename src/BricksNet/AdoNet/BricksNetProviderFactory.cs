using System.Data.Common;

namespace BricksNet.AdoNet;

/// <summary>
/// The <see cref="DbProviderFactory"/> implementation for BricksNet. Use
/// <see cref="Instance"/> to create provider-agnostic ADO.NET objects.
/// </summary>
public sealed class BricksNetProviderFactory : DbProviderFactory
{
    /// <summary>The singleton factory instance.</summary>
    public static readonly BricksNetProviderFactory Instance = new();

    private BricksNetProviderFactory()
    {
    }

    /// <inheritdoc />
    public override bool CanCreateDataSourceEnumerator => false;

    /// <inheritdoc />
    public override DbConnection CreateConnection() => new BricksNetConnection();

    /// <inheritdoc />
    public override DbCommand CreateCommand() => new BricksNetCommand();

    /// <inheritdoc />
    public override DbParameter CreateParameter() => new BricksNetParameter();

    /// <inheritdoc />
    public override DbConnectionStringBuilder CreateConnectionStringBuilder() =>
        new BricksNetConnectionStringBuilder();

    /// <inheritdoc />
    public override DbCommandBuilder CreateCommandBuilder() =>
        throw new NotSupportedException("BricksNet does not support DbCommandBuilder; write explicit INSERT/UPDATE/DELETE/MERGE statements instead.");

    /// <inheritdoc />
    public override DbDataAdapter CreateDataAdapter() =>
        throw new NotSupportedException("BricksNet does not support DbDataAdapter; use a DbDataReader or an ORM instead.");
}
