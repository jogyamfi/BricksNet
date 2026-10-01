using Microsoft.EntityFrameworkCore.Update;

namespace BricksNet.EntityFrameworkCore.Update.Internal;

/// <summary>
/// Generates INSERT/UPDATE/DELETE SQL for <c>SaveChanges</c>. EF resolves it while building the
/// model; the Databricks-specific DML is generated here once <c>SaveChanges</c> is supported.
/// </summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class BricksNetUpdateSqlGenerator : UpdateSqlGenerator
{
    /// <summary>Initialises a new instance.</summary>
    public BricksNetUpdateSqlGenerator(UpdateSqlGeneratorDependencies dependencies)
        : base(dependencies)
    {
    }
}
