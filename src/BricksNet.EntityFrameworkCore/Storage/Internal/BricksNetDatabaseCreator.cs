using Microsoft.EntityFrameworkCore.Storage;

namespace BricksNet.EntityFrameworkCore.Storage.Internal;

/// <summary>
/// Database creation for Databricks. Not supported yet: create catalogs, schemas and Delta tables
/// with SQL, outside EF Core.
/// </summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class BricksNetDatabaseCreator : RelationalDatabaseCreator
{
    internal const string NotSupportedMessage =
        "Creating, deleting or probing the database (EnsureCreated, EnsureDeleted, CanConnect) is not yet "
        + "supported by the BricksNet provider. Create Databricks schemas and Delta tables with SQL instead.";

    /// <summary>Initialises a new instance.</summary>
    public BricksNetDatabaseCreator(RelationalDatabaseCreatorDependencies dependencies)
        : base(dependencies)
    {
    }

    /// <summary>Not supported yet.</summary>
    /// <exception cref="NotSupportedException">Always.</exception>
    public override bool Exists() => throw new NotSupportedException(NotSupportedMessage);

    /// <summary>Not supported yet.</summary>
    /// <exception cref="NotSupportedException">Always.</exception>
    public override bool HasTables() => throw new NotSupportedException(NotSupportedMessage);

    /// <summary>Not supported yet.</summary>
    /// <exception cref="NotSupportedException">Always.</exception>
    public override void Create() => throw new NotSupportedException(NotSupportedMessage);

    /// <summary>Not supported yet.</summary>
    /// <exception cref="NotSupportedException">Always.</exception>
    public override void Delete() => throw new NotSupportedException(NotSupportedMessage);
}
