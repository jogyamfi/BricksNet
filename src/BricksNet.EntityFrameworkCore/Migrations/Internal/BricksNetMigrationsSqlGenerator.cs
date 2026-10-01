using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace BricksNet.EntityFrameworkCore.Migrations.Internal;

/// <summary>Migration SQL generation. Migrations are not supported by the BricksNet provider.</summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class BricksNetMigrationsSqlGenerator : MigrationsSqlGenerator
{
    /// <summary>Initialises a new instance.</summary>
    public BricksNetMigrationsSqlGenerator(MigrationsSqlGeneratorDependencies dependencies)
        : base(dependencies)
    {
    }

    /// <summary>Always throws: migrations are not supported.</summary>
    /// <exception cref="NotSupportedException">Always.</exception>
    public override IReadOnlyList<MigrationCommand> Generate(
        IReadOnlyList<MigrationOperation> operations,
        IModel? model = null,
        MigrationsSqlGenerationOptions options = MigrationsSqlGenerationOptions.Default) =>
        throw new NotSupportedException(BricksNetHistoryRepository.NotSupportedMessage);
}
