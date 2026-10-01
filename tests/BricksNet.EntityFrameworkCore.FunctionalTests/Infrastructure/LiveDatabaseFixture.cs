using System.Globalization;
using BricksNet.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Xunit;

namespace BricksNet.EntityFrameworkCore.FunctionalTests.Infrastructure;

/// <summary>
/// Creates a temporary schema in the configured catalog with <c>EnsureCreated</c>, seeds it, and
/// drops it afterwards. The seeded rows are also kept in memory so tests can compare server results
/// with LINQ-to-Objects over the same data.
/// </summary>
public sealed class LiveDatabaseFixture : IAsyncLifetime
{
    public string Catalog { get; } = LiveTestConfiguration.Catalog ?? "unused";

    public string Schema { get; } = string.Create(
        CultureInfo.InvariantCulture,
        $"efct_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..8]}");

    public IReadOnlyList<Customer> Customers { get; } = SeedData.Customers();

    public IReadOnlyList<Order> Orders { get; private set; } = [];

    public IReadOnlyList<Sample> Samples { get; } = SeedData.Samples();

    public IReadOnlyList<TypeRow> TypeRows { get; } = SeedData.TypeRows();

    public IReadOnlyList<BulkGroup> BulkGroups { get; } = SeedData.BulkGroups();

    public LiveContext CreateContext(
        Action<BricksNetDbContextOptionsBuilder>? bricksNetOptions = null,
        ILoggerFactory? loggerFactory = null)
    {
        var builder = new DbContextOptionsBuilder<LiveContext>().UseBricksNet(LiveTestConfiguration.ConnectionString, bricksNetOptions);
        if (loggerFactory is not null)
        {
            builder.UseLoggerFactory(loggerFactory);
        }

        return new LiveContext(builder.Options, Catalog, Schema);
    }

    public async Task InitializeAsync()
    {
        if (!LiveTestConfiguration.IsConfigured)
        {
            return;
        }

        Orders = Customers.SelectMany(c => c.Orders).ToList();

        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        // Seed from detached copies, so the in-memory lists stay free of EF tracking state.
        context.AddRange(Customers.Select(SeedData.CopyWithoutOrders));
        context.AddRange(Orders.Select(SeedData.Copy));
        context.AddRange(Samples);
        context.AddRange(TypeRows);
        context.AddRange(BulkGroups);
        await context.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        if (!LiveTestConfiguration.IsConfigured)
        {
            return;
        }

        // Identifiers cannot be parameters, so they are delimited (backticks escaped) instead.
        await using var context = CreateContext();
        var dropSchema = "DROP SCHEMA IF EXISTS " + Delimit(Catalog) + "." + Delimit(Schema) + " CASCADE";
        await context.Database.ExecuteSqlRawAsync(dropSchema);
    }

    private static string Delimit(string identifier) => "`" + identifier.Replace("`", "``", StringComparison.Ordinal) + "`";
}

/// <summary>Live tests share one database; the collection also runs them sequentially.</summary>
[CollectionDefinition(Name)]
public sealed class LiveCollection : ICollectionFixture<LiveDatabaseFixture>
{
    public const string Name = "Live Databricks";
}
