using Microsoft.EntityFrameworkCore.Update;

namespace BricksNet.EntityFrameworkCore.Update.Internal;

/// <summary>
/// Creates <c>SaveChanges</c> command batches. Saving changes is not supported yet; queries and raw
/// SQL (<c>ExecuteSql</c>) work.
/// </summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class BricksNetModificationCommandBatchFactory : IModificationCommandBatchFactory
{
    internal const string NotSupportedMessage =
        "SaveChanges is not yet supported by the BricksNet EF Core provider. Use ExecuteSql/ExecuteSqlAsync "
        + "to run INSERT, UPDATE, DELETE or MERGE statements.";

    /// <summary>Always throws: saving changes is not supported yet.</summary>
    /// <exception cref="NotSupportedException">Always.</exception>
    public virtual ModificationCommandBatch Create() => throw new NotSupportedException(NotSupportedMessage);
}
