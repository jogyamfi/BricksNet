using Microsoft.EntityFrameworkCore.Update;

namespace BricksNet.EntityFrameworkCore.Update.Internal;

/// <summary>
/// Creates <c>SaveChanges</c> command batches of one statement each: the SQL Statement Execution
/// API runs a single statement per request.
/// </summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class BricksNetModificationCommandBatchFactory : IModificationCommandBatchFactory
{
    private readonly ModificationCommandBatchFactoryDependencies _dependencies;

    /// <summary>Initialises a new instance.</summary>
    public BricksNetModificationCommandBatchFactory(ModificationCommandBatchFactoryDependencies dependencies)
    {
        _dependencies = dependencies;
    }

    /// <inheritdoc />
    public virtual ModificationCommandBatch Create() => new SingularModificationCommandBatch(_dependencies);
}
