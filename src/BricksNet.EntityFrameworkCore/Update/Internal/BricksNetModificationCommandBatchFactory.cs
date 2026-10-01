using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Update;

namespace BricksNet.EntityFrameworkCore.Update.Internal;

/// <summary>
/// Creates <c>SaveChanges</c> command batches. Each batch is one statement; consecutive inserts
/// into the same table are combined into one multi-row <c>INSERT</c> of up to <c>MaxBatchSize</c>
/// rows (default 1000; <c>MaxBatchSize(1)</c> turns combining off).
/// </summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class BricksNetModificationCommandBatchFactory : IModificationCommandBatchFactory
{
    /// <summary>The most rows in one combined insert when <c>MaxBatchSize</c> is not configured.</summary>
    public const int DefaultMaxBatchSize = 1000;

    private readonly ModificationCommandBatchFactoryDependencies _dependencies;
    private readonly int _maxBatchSize;

    /// <summary>Initialises a new instance.</summary>
    public BricksNetModificationCommandBatchFactory(ModificationCommandBatchFactoryDependencies dependencies, IDbContextOptions options)
    {
        _dependencies = dependencies;
        _maxBatchSize = Math.Max(1, RelationalOptionsExtension.Extract(options).MaxBatchSize ?? DefaultMaxBatchSize);
    }

    /// <inheritdoc />
    public virtual ModificationCommandBatch Create() => new BricksNetModificationCommandBatch(_dependencies, _maxBatchSize);
}
