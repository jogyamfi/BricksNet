using Microsoft.EntityFrameworkCore.Migrations;

namespace BricksNet.EntityFrameworkCore.Migrations.Internal;

/// <summary>Migrations history. Migrations are not supported by the BricksNet provider.</summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class BricksNetHistoryRepository : HistoryRepository
{
    internal const string NotSupportedMessage =
        "Migrations are not supported by the BricksNet EF Core provider. Manage Databricks schemas and "
        + "Delta tables with SQL, outside EF Core.";

    /// <summary>Initialises a new instance.</summary>
    public BricksNetHistoryRepository(HistoryRepositoryDependencies dependencies)
        : base(dependencies)
    {
    }

    /// <inheritdoc />
    public override LockReleaseBehavior LockReleaseBehavior => LockReleaseBehavior.Explicit;

    /// <inheritdoc />
    protected override string ExistsSql => throw NotSupported();

    /// <inheritdoc />
    public override bool Exists() => throw NotSupported();

    /// <inheritdoc />
    public override Task<bool> ExistsAsync(CancellationToken cancellationToken = default) => throw NotSupported();

    /// <inheritdoc />
    protected override bool InterpretExistsResult(object? value) => throw NotSupported();

    /// <inheritdoc />
    public override string GetCreateIfNotExistsScript() => throw NotSupported();

    /// <inheritdoc />
    public override string GetBeginIfNotExistsScript(string migrationId) => throw NotSupported();

    /// <inheritdoc />
    public override string GetBeginIfExistsScript(string migrationId) => throw NotSupported();

    /// <inheritdoc />
    public override string GetEndIfScript() => throw NotSupported();

    /// <inheritdoc />
    public override IMigrationsDatabaseLock AcquireDatabaseLock() => throw NotSupported();

    /// <inheritdoc />
    public override Task<IMigrationsDatabaseLock> AcquireDatabaseLockAsync(CancellationToken cancellationToken = default) =>
        throw NotSupported();

    private static NotSupportedException NotSupported() => new(NotSupportedMessage);
}
