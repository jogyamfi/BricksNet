using System.Data.Common;
using BricksNet.Contracts.Options;
using BricksNet.EntityFrameworkCore.Infrastructure;
using BricksNet.EntityFrameworkCore.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

// Extension methods live in EF's namespace so UseBricksNet is discoverable next to UseSqlServer etc.
namespace Microsoft.EntityFrameworkCore;

/// <summary>BricksNet (Databricks SQL) specific extension methods for <see cref="DbContextOptionsBuilder"/>.</summary>
public static class BricksNetDbContextOptionsBuilderExtensions
{
    /// <summary>
    /// Configures the context to connect to Databricks SQL. The connection string must be set
    /// later, for example with <c>context.Database.SetConnectionString</c>.
    /// </summary>
    /// <param name="optionsBuilder">The builder being used to configure the context.</param>
    /// <param name="bricksNetOptionsAction">An optional action to allow additional BricksNet-specific configuration.</param>
    /// <returns>The options builder so that further configuration can be chained.</returns>
    public static DbContextOptionsBuilder UseBricksNet(
        this DbContextOptionsBuilder optionsBuilder,
        Action<BricksNetDbContextOptionsBuilder>? bricksNetOptionsAction = null)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);

        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(GetOrCreateExtension(optionsBuilder));
        return ApplyConfiguration(optionsBuilder, bricksNetOptionsAction);
    }

    /// <summary>Configures the context to connect to Databricks SQL with a BricksNet connection string.</summary>
    /// <param name="optionsBuilder">The builder being used to configure the context.</param>
    /// <param name="connectionString">
    /// The BricksNet connection string, e.g.
    /// <c>Host=adb-123.azuredatabricks.net;HttpPath=/sql/1.0/warehouses/abc;Token=...;Catalog=main;Schema=sales</c>.
    /// </param>
    /// <param name="bricksNetOptionsAction">An optional action to allow additional BricksNet-specific configuration.</param>
    /// <returns>The options builder so that further configuration can be chained.</returns>
    public static DbContextOptionsBuilder UseBricksNet(
        this DbContextOptionsBuilder optionsBuilder,
        string? connectionString,
        Action<BricksNetDbContextOptionsBuilder>? bricksNetOptionsAction = null)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);

        var extension = (BricksNetOptionsExtension)GetOrCreateExtension(optionsBuilder).WithConnectionString(connectionString);
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);
        return ApplyConfiguration(optionsBuilder, bricksNetOptionsAction);
    }

    /// <summary>
    /// Configures the context to use an existing <see cref="DbConnection"/>, which must be a
    /// <see cref="BricksNet.AdoNet.BricksNetConnection"/>. The context does not dispose it.
    /// </summary>
    /// <param name="optionsBuilder">The builder being used to configure the context.</param>
    /// <param name="connection">The connection to use.</param>
    /// <param name="bricksNetOptionsAction">An optional action to allow additional BricksNet-specific configuration.</param>
    /// <returns>The options builder so that further configuration can be chained.</returns>
    public static DbContextOptionsBuilder UseBricksNet(
        this DbContextOptionsBuilder optionsBuilder,
        DbConnection connection,
        Action<BricksNetDbContextOptionsBuilder>? bricksNetOptionsAction = null) =>
        UseBricksNet(optionsBuilder, connection, contextOwnsConnection: false, bricksNetOptionsAction);

    /// <summary>
    /// Configures the context to use an existing <see cref="DbConnection"/>, which must be a
    /// <see cref="BricksNet.AdoNet.BricksNetConnection"/>.
    /// </summary>
    /// <param name="optionsBuilder">The builder being used to configure the context.</param>
    /// <param name="connection">The connection to use.</param>
    /// <param name="contextOwnsConnection">
    /// <see langword="true"/> to dispose the connection when the context is disposed.
    /// </param>
    /// <param name="bricksNetOptionsAction">An optional action to allow additional BricksNet-specific configuration.</param>
    /// <returns>The options builder so that further configuration can be chained.</returns>
    public static DbContextOptionsBuilder UseBricksNet(
        this DbContextOptionsBuilder optionsBuilder,
        DbConnection connection,
        bool contextOwnsConnection,
        Action<BricksNetDbContextOptionsBuilder>? bricksNetOptionsAction = null)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentNullException.ThrowIfNull(connection);

        var extension = (BricksNetOptionsExtension)GetOrCreateExtension(optionsBuilder)
            .WithConnection(connection, contextOwnsConnection);
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);
        return ApplyConfiguration(optionsBuilder, bricksNetOptionsAction);
    }

    /// <summary>Configures the context to connect to Databricks SQL with pre-built client options.</summary>
    /// <param name="optionsBuilder">The builder being used to configure the context.</param>
    /// <param name="clientOptions">The BricksNet client options (host, HTTP path, authentication, ...).</param>
    /// <param name="accessToken">
    /// The personal access token, or the client secret when <see cref="BricksNetClientOptions.AuthType"/>
    /// is <see cref="AuthType.OAuthClientCredentials"/>.
    /// </param>
    /// <param name="bricksNetOptionsAction">An optional action to allow additional BricksNet-specific configuration.</param>
    /// <returns>The options builder so that further configuration can be chained.</returns>
    public static DbContextOptionsBuilder UseBricksNet(
        this DbContextOptionsBuilder optionsBuilder,
        BricksNetClientOptions clientOptions,
        string? accessToken,
        Action<BricksNetDbContextOptionsBuilder>? bricksNetOptionsAction = null)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentNullException.ThrowIfNull(clientOptions);

        var extension = GetOrCreateExtension(optionsBuilder).WithClientOptions(clientOptions, accessToken);
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);
        return ApplyConfiguration(optionsBuilder, bricksNetOptionsAction);
    }

    /// <inheritdoc cref="UseBricksNet(DbContextOptionsBuilder, Action{BricksNetDbContextOptionsBuilder}?)"/>
    public static DbContextOptionsBuilder<TContext> UseBricksNet<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        Action<BricksNetDbContextOptionsBuilder>? bricksNetOptionsAction = null)
        where TContext : DbContext =>
        (DbContextOptionsBuilder<TContext>)UseBricksNet((DbContextOptionsBuilder)optionsBuilder, bricksNetOptionsAction);

    /// <inheritdoc cref="UseBricksNet(DbContextOptionsBuilder, string?, Action{BricksNetDbContextOptionsBuilder}?)"/>
    public static DbContextOptionsBuilder<TContext> UseBricksNet<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        string? connectionString,
        Action<BricksNetDbContextOptionsBuilder>? bricksNetOptionsAction = null)
        where TContext : DbContext =>
        (DbContextOptionsBuilder<TContext>)UseBricksNet(
            (DbContextOptionsBuilder)optionsBuilder, connectionString, bricksNetOptionsAction);

    /// <inheritdoc cref="UseBricksNet(DbContextOptionsBuilder, DbConnection, Action{BricksNetDbContextOptionsBuilder}?)"/>
    public static DbContextOptionsBuilder<TContext> UseBricksNet<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        DbConnection connection,
        Action<BricksNetDbContextOptionsBuilder>? bricksNetOptionsAction = null)
        where TContext : DbContext =>
        (DbContextOptionsBuilder<TContext>)UseBricksNet((DbContextOptionsBuilder)optionsBuilder, connection, bricksNetOptionsAction);

    /// <inheritdoc cref="UseBricksNet(DbContextOptionsBuilder, DbConnection, bool, Action{BricksNetDbContextOptionsBuilder}?)"/>
    public static DbContextOptionsBuilder<TContext> UseBricksNet<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        DbConnection connection,
        bool contextOwnsConnection,
        Action<BricksNetDbContextOptionsBuilder>? bricksNetOptionsAction = null)
        where TContext : DbContext =>
        (DbContextOptionsBuilder<TContext>)UseBricksNet(
            (DbContextOptionsBuilder)optionsBuilder, connection, contextOwnsConnection, bricksNetOptionsAction);

    /// <inheritdoc cref="UseBricksNet(DbContextOptionsBuilder, BricksNetClientOptions, string?, Action{BricksNetDbContextOptionsBuilder}?)"/>
    public static DbContextOptionsBuilder<TContext> UseBricksNet<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        BricksNetClientOptions clientOptions,
        string? accessToken,
        Action<BricksNetDbContextOptionsBuilder>? bricksNetOptionsAction = null)
        where TContext : DbContext =>
        (DbContextOptionsBuilder<TContext>)UseBricksNet(
            (DbContextOptionsBuilder)optionsBuilder, clientOptions, accessToken, bricksNetOptionsAction);

    private static BricksNetOptionsExtension GetOrCreateExtension(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.Options.FindExtension<BricksNetOptionsExtension>() ?? new BricksNetOptionsExtension();

    private static DbContextOptionsBuilder ApplyConfiguration(
        DbContextOptionsBuilder optionsBuilder,
        Action<BricksNetDbContextOptionsBuilder>? bricksNetOptionsAction)
    {
        // Databricks cannot take part in System.Transactions; fail instead of silently ignoring a scope.
        var coreOptionsExtension = optionsBuilder.Options.FindExtension<CoreOptionsExtension>() ?? new CoreOptionsExtension();
        coreOptionsExtension = coreOptionsExtension.WithWarningsConfiguration(
            coreOptionsExtension.WarningsConfiguration.TryWithExplicit(
                RelationalEventId.AmbientTransactionWarning, WarningBehavior.Throw));
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(coreOptionsExtension);

        bricksNetOptionsAction?.Invoke(new BricksNetDbContextOptionsBuilder(optionsBuilder));
        return optionsBuilder;
    }
}
