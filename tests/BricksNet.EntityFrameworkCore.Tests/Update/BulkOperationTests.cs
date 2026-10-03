using BricksNet.EntityFrameworkCore.Query;
using BricksNet.EntityFrameworkCore.TestUtilities;
using BricksNet.EntityFrameworkCore.Update.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Xunit;

namespace BricksNet.EntityFrameworkCore.Update;

/// <summary>ExecuteUpdate/ExecuteDelete, multi-row inserts and compiled queries.</summary>
public sealed class BulkOperationTests : QueryTestBase
{
    private sealed class Account
    {
        public int Id { get; set; }

        public string Owner { get; set; } = string.Empty;

        public string? Note { get; set; }

        public long Version { get; set; }
    }

    private ConfigurableContext CreateAccountContext(
        Action<BricksNet.EntityFrameworkCore.Infrastructure.BricksNetDbContextOptionsBuilder>? bricksNetOptions = null,
        ILoggerFactory? loggerFactory = null,
        string? catalog = null) =>
        ConfigurableContext.Create(
            Databricks,
            m =>
            {
                if (catalog is not null)
                {
                    m.HasDefaultCatalog(catalog).HasDefaultSchema("ops");
                }

                m.Entity<Account>().ToTable("accounts");
            },
            loggerFactory,
            bricksNetOptions);

    private static Account[] Accounts(int count) =>
        Enumerable.Range(1, count).Select(i => new Account { Id = i, Owner = "owner" + i }).ToArray();

    // ---- ExecuteDelete ----

    [Fact]
    public async Task ExecuteDelete_ReturnsAffectedRows()
    {
        Databricks.Responder = _ => FakeResult.AffectedRows(4);
        await using var context = CreateContext();

        Assert.Equal(4, await context.Orders.Where(o => o.Amount < 10).ExecuteDeleteAsync());
        await context.Orders.Where(o => o.Customer!.City == "Oslo").ExecuteDeleteAsync();
        await context.Orders.OrderBy(o => o.Id).Take(10).ExecuteDeleteAsync();

        AssertSql(
            """
            DELETE FROM `orders` AS `o`
            WHERE `o`.`Amount` < 10BD
            """,
            """
            DELETE FROM `orders` AS `o`
            WHERE `o`.`Id` IN (
                SELECT `o0`.`Id`
                FROM `orders` AS `o0`
                INNER JOIN `customers` AS `c` ON `o0`.`CustomerId` = `c`.`Id`
                WHERE `c`.`City` = 'Oslo'
            )
            """,
            """
            DELETE FROM `orders` AS `o`
            WHERE `o`.`Id` IN (
                SELECT `o0`.`Id`
                FROM `orders` AS `o0`
                ORDER BY `o0`.`Id`
                LIMIT 10
            )
            """);
    }

    // ---- ExecuteUpdate ----

    [Fact]
    public async Task ExecuteUpdate_SingleTable()
    {
        Databricks.Responder = _ => FakeResult.AffectedRows(3);
        await using var context = CreateContext();

        Assert.Equal(3, await context.Orders
            .Where(o => o.Amount < 10)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, "small").SetProperty(o => o.Quantity, o => o.Quantity + 1)));
        await context.Customers.ExecuteUpdateAsync(s => s.SetProperty(c => c.Address.Street, "unknown"));

        AssertSql(
            """
            UPDATE `orders` AS `o`
            SET `Status` = :p,
                `Quantity` = `o`.`Quantity` + 1
            WHERE `o`.`Amount` < 10BD
            """,
            """
            UPDATE `customers` AS `c`
            SET `Address_Street` = :p
            """);
    }

    [Fact]
    public async Task ExecuteUpdate_WithJoins_UsesExistsInsteadOfUpdateFrom()
    {
        Databricks.Responder = _ => FakeResult.AffectedRows(1);
        await using var context = CreateContext();

        await context.Orders.Where(o => o.Customer!.City == "Oslo").ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, "nordic"));
        await context.Orders.OrderBy(o => o.Id).Take(5).ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, "first"));

        AssertSql(
            """
            UPDATE `orders` AS `o`
            SET `Status` = :p
            WHERE EXISTS (
                SELECT 1
                FROM `customers` AS `c`
                WHERE `o`.`CustomerId` = `c`.`Id` AND `c`.`City` = 'Oslo')
            """,
            """
            UPDATE `orders` AS `o0`
            SET `Status` = :p1
            WHERE EXISTS (
                SELECT 1
                FROM (
                    SELECT `o`.`Id`
                    FROM `orders` AS `o`
                    ORDER BY `o`.`Id`
                    LIMIT 5
                ) AS `o1`
                WHERE `o0`.`Id` = `o1`.`Id`)
            """);
    }

    [Fact]
    public async Task ExecuteUpdateAndDelete_UseCatalogNames()
    {
        Databricks.Responder = _ => FakeResult.AffectedRows(1);
        await using var context = CreateAccountContext(catalog: "main");

        await context.Set<Account>().Where(a => a.Id == 1).ExecuteUpdateAsync(s => s.SetProperty(a => a.Note, "x"));
        await context.Set<Account>().Where(a => a.Id == 1).ExecuteDeleteAsync();

        Assert.StartsWith("UPDATE `main`.`ops`.`accounts` AS `a`", Databricks.Statements[0].Sql, StringComparison.Ordinal);
        Assert.StartsWith("DELETE FROM `main`.`ops`.`accounts` AS `a`", Databricks.Statements[1].Sql, StringComparison.Ordinal);
    }

    // ---- multi-row inserts ----

    [Fact]
    public async Task Inserts_IntoTheSameTable_BecomeOneStatement()
    {
        Databricks.Responder = _ => FakeResult.AffectedRows(3);
        using var provider = new CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(b => b.AddProvider(provider));
        await using var context = CreateAccountContext(loggerFactory: loggerFactory);
        var accounts = Accounts(3);
        context.AddRange(accounts);

        Assert.Equal(3, await context.SaveChangesAsync());

        AssertSql(
            """
            INSERT INTO `accounts` (`Id`, `Note`, `Owner`, `Version`)
            VALUES (:p0, :p1, :p2, :p3),
            (:p4, :p5, :p6, :p7),
            (:p8, :p9, :p10, :p11)
            """);
        Assert.Equal(12, Databricks.Statements[0].Parameters.Count);
        Assert.All(accounts, a => Assert.Equal(EntityState.Unchanged, context.Entry(a).State));

        // One statement is atomic, so there is nothing to warn about.
        Assert.DoesNotContain(provider.Messages, m => m.Message.Contains("more than one statement", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MultiRowInsert_ChecksTheTotalRowCount()
    {
        Databricks.Responder = _ => FakeResult.AffectedRows(2);
        await using var context = CreateAccountContext();
        context.AddRange(Accounts(3));

        var ex = await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => context.SaveChangesAsync());

        Assert.Contains("expected to affect 3 row(s)", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MaxBatchSize_LimitsRowsPerInsert()
    {
        Databricks.Responder = s => FakeResult.AffectedRows(s.Parameters.Count / 4);
        await using var context = CreateAccountContext(o => o.MaxBatchSize(2));
        context.AddRange(Accounts(5));

        await context.SaveChangesAsync();

        Assert.Equal([8, 8, 4], Databricks.Statements.Select(s => s.Parameters.Count));
    }

    [Fact]
    public async Task ParameterBudget_SplitsLargeInserts()
    {
        Databricks.Responder = s => FakeResult.AffectedRows(s.Parameters.Count / 4);
        await using var context = CreateAccountContext();
        context.AddRange(Accounts(70));

        await context.SaveChangesAsync();

        // Four parameters per row: 64 rows fill the 256-parameter budget.
        Assert.Equal([BricksNetModificationCommandBatch.MaxParameters, 24], Databricks.Statements.Select(s => s.Parameters.Count));
    }

    [Fact]
    public async Task UpdatesAndDeletes_AreNotCombined()
    {
        Databricks.Responder = _ => FakeResult.AffectedRows(1);
        await using var context = CreateAccountContext();
        var existing = new Account { Id = 10, Owner = "x" };
        var removed = new Account { Id = 11, Owner = "y" };
        context.AttachRange(existing, removed);
        existing.Note = "changed";
        context.Remove(removed);
        context.Add(new Account { Id = 12, Owner = "z" });

        await context.SaveChangesAsync();

        Assert.Equal(3, Databricks.Statements.Count);
        Assert.Contains(Databricks.Statements, s => s.Sql.StartsWith("UPDATE", StringComparison.Ordinal));
        Assert.Contains(Databricks.Statements, s => s.Sql.StartsWith("DELETE", StringComparison.Ordinal));
        Assert.Contains(Databricks.Statements, s => s.Sql.StartsWith("INSERT", StringComparison.Ordinal));
    }

    // ---- compiled queries ----

    [Fact]
    public async Task CompiledQuery_RunsWithDifferentParameters()
    {
        var query = EF.CompileAsyncQuery((ShopContext context, int minimum) => context.Orders.Where(o => o.Quantity >= minimum));
        Databricks.Responder = _ => FakeResult.Table([("Id", "INT")]);
        await using var context = CreateContext();

        await foreach (var _ in query(context, 1))
        {
        }

        await foreach (var _ in query(context, 5))
        {
        }

        Assert.Equal([1, 5], Databricks.Statements.Select(s => s.Parameters.Single().Value));
        Assert.Equal(Databricks.Statements[0].Sql, Databricks.Statements[1].Sql);
    }
}
