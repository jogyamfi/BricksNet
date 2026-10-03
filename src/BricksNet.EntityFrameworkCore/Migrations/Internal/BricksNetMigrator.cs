using Microsoft.EntityFrameworkCore.Migrations;

namespace BricksNet.EntityFrameworkCore.Migrations.Internal;

/// <summary>
/// Replaces EF's migrator so that <c>Migrate</c> and script generation report that migrations
/// are unsupported, rather than failing on an earlier step such as probing for the database.
/// </summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class BricksNetMigrator : IMigrator
{
    /// <summary>Always throws: migrations are not supported.</summary>
    /// <exception cref="NotSupportedException">Always.</exception>
    public virtual void Migrate(string? targetMigration) => throw NotSupported();

    /// <summary>Always throws: migrations are not supported.</summary>
    /// <exception cref="NotSupportedException">Always.</exception>
    public virtual Task MigrateAsync(string? targetMigration, CancellationToken cancellationToken = default) =>
        throw NotSupported();

    /// <summary>Always throws: migrations are not supported.</summary>
    /// <exception cref="NotSupportedException">Always.</exception>
    public virtual string GenerateScript(
        string? fromMigration = null,
        string? toMigration = null,
        MigrationsSqlGenerationOptions options = MigrationsSqlGenerationOptions.Default) =>
        throw NotSupported();

    /// <summary>Always throws: migrations are not supported.</summary>
    /// <exception cref="NotSupportedException">Always.</exception>
    public virtual bool HasPendingModelChanges() => throw NotSupported();

    private static NotSupportedException NotSupported() => new(BricksNetHistoryRepository.NotSupportedMessage);
}
