using BricksNet.EntityFrameworkCore.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BricksNet.EntityFrameworkCore.Storage;

/// <summary>EnsureCreated / EnsureDeleted / GenerateCreateScript for development and tests.</summary>
public sealed class DatabaseCreatorTests
{
    private sealed class Product
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public decimal? Price { get; set; }

        public DateTime Created { get; set; }
    }

    private sealed class Tag
    {
        public Guid Id { get; set; }

        public string Label { get; set; } = string.Empty;
    }

    private static void Model(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultCatalog("main").HasDefaultSchema("shop");
        modelBuilder.Entity<Product>(product =>
        {
            product.ToTable("products", t => t.HasComment("Catalogue of products"));
            product.Property(p => p.Name).HasComment("Display name, it's shown to users");
            product.Property(p => p.Price).HasPrecision(10, 2);
            product.HasIndex(p => p.Name);
        });
        modelBuilder.Entity<Tag>().ToTable("tags");
    }

    private static FakeResult Respond(RecordedStatement statement, bool tablesExist) =>
        statement.Sql.StartsWith("SELECT 1", StringComparison.Ordinal) ? FakeResult.Table([("1", "INT")], [1])
        : statement.Sql.StartsWith("SHOW SCHEMAS", StringComparison.Ordinal) && tablesExist ? FakeResult.Table([("databaseName", "STRING")], ["shop"])
        : statement.Sql.StartsWith("SHOW TABLES", StringComparison.Ordinal) && tablesExist
            ? FakeResult.Table([("database", "STRING"), ("tableName", "STRING"), ("isTemporary", "BOOLEAN")], ["shop", "products", false])
            : FakeResult.Empty;

    [Fact]
    public void GenerateCreateScript_CreatesDeltaTables_WithoutConstraintsOrIndexes()
    {
        using var context = ConfigurableContext.Create(new FakeDatabricks(), Model);

        var script = context.Database.GenerateCreateScript().ReplaceLineEndings("\n");

        Assert.Equal(
            """
            CREATE SCHEMA IF NOT EXISTS `main`.`shop`;
            CREATE TABLE `main`.`shop`.`products` (
                `Id` INT NOT NULL,
                `Name` STRING NOT NULL COMMENT 'Display name, it\'s shown to users',
                `Price` DECIMAL(10,2),
                `Created` TIMESTAMP_NTZ NOT NULL
            ) USING DELTA
            COMMENT 'Catalogue of products';
            CREATE TABLE `main`.`shop`.`tags` (
                `Id` STRING NOT NULL,
                `Label` STRING NOT NULL
            ) USING DELTA;
            """.ReplaceLineEndings("\n").Trim(),
            script.Trim());
    }

    [Fact]
    public async Task EnsureCreated_CreatesSchemaAndTables_WhenNoneExist()
    {
        var databricks = new FakeDatabricks { Responder = s => Respond(s, tablesExist: false) };
        await using var context = ConfigurableContext.Create(databricks, Model);

        Assert.True(await context.Database.EnsureCreatedAsync());

        Assert.Equal(
            ["SELECT 1", "SHOW SCHEMAS IN `main` LIKE 'shop'", "CREATE SCHEMA", "CREATE TABLE", "CREATE TABLE"],
            databricks.Statements.Select(s => s.Sql.StartsWith("CREATE", StringComparison.Ordinal) ? string.Join(' ', s.Sql.Split(' ').Take(2)) : s.Sql.Trim()));
    }

    [Fact]
    public async Task EnsureCreated_DoesNothing_WhenTablesExist()
    {
        var databricks = new FakeDatabricks { Responder = s => Respond(s, tablesExist: true) };
        await using var context = ConfigurableContext.Create(databricks, Model);

        Assert.False(await context.Database.EnsureCreatedAsync());

        Assert.DoesNotContain(databricks.Statements, s => s.Sql.StartsWith("CREATE", StringComparison.Ordinal));
        Assert.Contains(databricks.Statements, s => s.Sql.Trim() == "SHOW TABLES IN `main`.`shop`");
    }

    [Fact]
    public async Task EnsureDeleted_DropsOnlyTheModelsTables()
    {
        var databricks = new FakeDatabricks { Responder = s => Respond(s, tablesExist: true) };
        await using var context = ConfigurableContext.Create(databricks, Model);

        Assert.True(await context.Database.EnsureDeletedAsync());

        var drops = databricks.Statements.Where(s => s.Sql.StartsWith("DROP", StringComparison.Ordinal)).Select(s => s.Sql.Trim());
        Assert.Equal(["DROP TABLE IF EXISTS `main`.`shop`.`products`", "DROP TABLE IF EXISTS `main`.`shop`.`tags`"], drops);
        Assert.DoesNotContain(databricks.Statements, s => s.Sql.Contains("SCHEMA", StringComparison.Ordinal) && s.Sql.StartsWith("DROP", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CanConnect_IsFalse_WhenTheServerFails()
    {
        var databricks = new FakeDatabricks { Responder = _ => throw new BricksNet.AdoNet.BricksNetException("unreachable") };
        await using var context = ConfigurableContext.Create(databricks, Model);

        Assert.False(await context.Database.CanConnectAsync());
    }
}
