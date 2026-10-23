using BricksNet.EntityFrameworkCore.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BricksNet.EntityFrameworkCore.Metadata;

/// <summary>Unity Catalog three-part names (<c>catalog.schema.table</c>) and views.</summary>
public sealed class CatalogTests
{
    private sealed class Sale
    {
        public int Id { get; set; }

        public decimal Amount { get; set; }
    }

    private sealed class Rate
    {
        public string Currency { get; set; } = string.Empty;

        public decimal Value { get; set; }
    }

    private static async Task<string> QuerySql<T>(Action<ModelBuilder> configure)
        where T : class
    {
        var databricks = new FakeDatabricks();
        await using var context = ConfigurableContext.Create(databricks, configure);

        await context.Set<T>().ToListAsync();

        return Assert.Single(databricks.Statements).Sql.ReplaceLineEndings("\n");
    }

    [Fact]
    public async Task DefaultCatalog_QualifiesEveryTable()
    {
        var sql = await QuerySql<Sale>(m => m.HasDefaultCatalog("main").Entity<Sale>().ToTable("sales", "retail"));

        Assert.Contains("FROM `main`.`retail`.`sales` AS `s`", sql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EntityCatalog_OverridesTheDefault()
    {
        var sql = await QuerySql<Sale>(m => m
            .HasDefaultCatalog("main")
            .HasDefaultSchema("retail")
            .Entity<Sale>().HasCatalog("archive").ToTable("sales"));

        Assert.Contains("FROM `archive`.`retail`.`sales` AS `s`", sql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoCatalog_UsesTheConnectionsCatalog()
    {
        var sql = await QuerySql<Sale>(m => m.Entity<Sale>().ToTable("sales", "retail"));

        Assert.Contains("FROM `retail`.`sales` AS `s`", sql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task KeylessView_IsQueryable_WithCatalog()
    {
        var sql = await QuerySql<Rate>(m => m.HasDefaultCatalog("main").Entity<Rate>().HasNoKey().ToView("fx_rates", "ref"));

        Assert.Equal(
            """
            SELECT `f`.`Currency`, `f`.`Value`
            FROM `main`.`ref`.`fx_rates` AS `f`
            """.ReplaceLineEndings("\n"),
            sql.Trim().ReplaceLineEndings("\n"));
    }

    [Fact]
    public void GetCatalog_ReadsEntityThenModelAnnotation()
    {
        using var context = ConfigurableContext.Create(new FakeDatabricks(), m =>
        {
            m.HasDefaultCatalog("main").HasDefaultSchema("retail");
            m.Entity<Sale>();
            m.Entity<Rate>().HasNoKey().HasCatalog("ref");
        });

        Assert.Equal("main", context.Model.GetDefaultCatalog());
        Assert.Equal("main", context.Model.FindEntityType(typeof(Sale))!.GetCatalog());
        Assert.Equal("ref", context.Model.FindEntityType(typeof(Rate))!.GetCatalog());
    }
}
