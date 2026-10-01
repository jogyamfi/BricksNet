using BricksNet.EntityFrameworkCore.Diagnostics.Internal;
using BricksNet.EntityFrameworkCore.Infrastructure.Internal;
using BricksNet.EntityFrameworkCore.Metadata.Conventions;
using BricksNet.EntityFrameworkCore.Migrations.Internal;
using BricksNet.EntityFrameworkCore.Storage.Internal;
using BricksNet.EntityFrameworkCore.Update.Internal;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update;

// Lives in the DI namespace, next to the other AddEntityFramework* provider registrations.
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>BricksNet-specific extension methods for <see cref="IServiceCollection"/>.</summary>
public static class BricksNetServiceCollectionExtensions
{
    /// <summary>
    /// Registers the services EF Core needs to use the BricksNet (Databricks SQL) provider.
    /// </summary>
    /// <remarks>
    /// Applications do not normally call this: <c>UseBricksNet</c> registers the provider in EF's
    /// internal service provider. Call it only when supplying your own internal service provider via
    /// <c>DbContextOptionsBuilder.UseInternalServiceProvider</c>.
    /// </remarks>
    /// <param name="serviceCollection">The service collection to add services to.</param>
    /// <returns>The same service collection so that multiple calls can be chained.</returns>
    public static IServiceCollection AddEntityFrameworkBricksNet(this IServiceCollection serviceCollection)
    {
        ArgumentNullException.ThrowIfNull(serviceCollection);

        new EntityFrameworkRelationalServicesBuilder(serviceCollection)
            .TryAdd<LoggingDefinitions, BricksNetLoggingDefinitions>()
            .TryAdd<IDatabaseProvider, DatabaseProvider<BricksNetOptionsExtension>>()
            .TryAdd<IRelationalTypeMappingSource, BricksNetTypeMappingSource>()
            .TryAdd<ISqlGenerationHelper, BricksNetSqlGenerationHelper>()
            .TryAdd<IProviderConventionSetBuilder, BricksNetConventionSetBuilder>()
            .TryAdd<IRelationalConnection, BricksNetRelationalConnection>()
            .TryAdd<IRelationalDatabaseCreator, BricksNetDatabaseCreator>()
            .TryAdd<IHistoryRepository, BricksNetHistoryRepository>()
            .TryAdd<IMigrationsSqlGenerator, BricksNetMigrationsSqlGenerator>()
            .TryAdd<IMigrator, BricksNetMigrator>()
            .TryAdd<IUpdateSqlGenerator, BricksNetUpdateSqlGenerator>()
            .TryAdd<IBatchExecutor, BricksNetBatchExecutor>()
            .TryAdd<IModificationCommandBatchFactory, BricksNetModificationCommandBatchFactory>()
            .TryAddCoreServices();

        return serviceCollection;
    }
}
