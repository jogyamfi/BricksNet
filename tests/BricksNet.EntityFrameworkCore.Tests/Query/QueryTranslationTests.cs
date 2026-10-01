using BricksNet.EntityFrameworkCore.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BricksNet.EntityFrameworkCore.Query;

/// <summary>SQL baselines for the core LINQ operators, as Databricks SQL.</summary>
public sealed class QueryTranslationTests : QueryTestBase
{
    private const string OrderColumns = "`o`.`Id`, `o`.`Amount`, `o`.`CustomerId`, `o`.`OrderDate`, `o`.`Quantity`, `o`.`Status`";
    private const string CustomerColumns = "`c`.`Id`, `c`.`City`, `c`.`IsActive`, `c`.`Name`, `c`.`Address_PostCode`, `c`.`Address_Street`";

    // ---- filtering, ordering and paging ----

    [Fact]
    public async Task Where_WithParameter()
    {
        await using var context = CreateContext();
        var city = "Oslo";

        await context.Customers.Where(c => c.IsActive && c.City == city).ToListAsync();

        AssertSql(
            $"""
            SELECT {CustomerColumns}
            FROM `customers` AS `c`
            WHERE `c`.`IsActive` AND `c`.`City` = :city
            """);
        Assert.Equal(["city"], Assert.Single(Databricks.Statements).Parameters.Select(p => p.Name));
    }

    [Fact]
    public async Task OrderBy_Skip_Take_InlinesPagingValues()
    {
        await using var context = CreateContext();
        var skip = 10;
        var take = 5;

        await context.Orders.OrderBy(o => o.OrderDate).ThenByDescending(o => o.Id).Skip(skip).Take(take).ToListAsync();

        AssertSql(
            $"""
            SELECT {OrderColumns}
            FROM `orders` AS `o`
            ORDER BY `o`.`OrderDate`, `o`.`Id` DESC
            LIMIT 5
            OFFSET 10
            """);
        Assert.Empty(Assert.Single(Databricks.Statements).Parameters);
    }

    [Fact]
    public async Task Skip_WithoutTake_UsesOffsetAlone()
    {
        await using var context = CreateContext();

        await context.Orders.OrderBy(o => o.Id).Skip(10).ToListAsync();

        AssertSql(
            $"""
            SELECT {OrderColumns}
            FROM `orders` AS `o`
            ORDER BY `o`.`Id`
            OFFSET 10
            """);
    }

    [Fact]
    public async Task Take_DifferentValues_ProduceDifferentSql()
    {
        await using var context = CreateContext();

        foreach (var take in new[] { 3, 7 })
        {
            await context.Orders.Take(take).ToListAsync();
        }

        Assert.Equal(
            ["LIMIT 3", "LIMIT 7"],
            Databricks.Statements.Select(s => s.Sql.ReplaceLineEndings("\n").Trim().Split('\n')[^1]));
    }

    [Fact]
    public async Task FirstOrDefault_UsesLimit1()
    {
        await using var context = CreateContext();

        await context.Orders.OrderBy(o => o.Id).FirstOrDefaultAsync();

        AssertSql(
            $"""
            SELECT {OrderColumns}
            FROM `orders` AS `o`
            ORDER BY `o`.`Id`
            LIMIT 1
            """);
    }

    [Fact]
    public async Task SingleOrDefault_UsesLimit2()
    {
        await using var context = CreateContext();

        await context.Orders.SingleOrDefaultAsync(o => o.Id == 1);

        AssertSql(
            $"""
            SELECT {OrderColumns}
            FROM `orders` AS `o`
            WHERE `o`.`Id` = 1
            LIMIT 2
            """);
    }

    // ---- predicates ----

    [Fact]
    public async Task Any_And_All_UseExists()
    {
        Databricks.Responder = _ => FakeResult.Table([("Value", "BOOLEAN")], [true]);
        await using var context = CreateContext();

        Assert.True(await context.Orders.AnyAsync(o => o.Amount > 10));
        Assert.True(await context.Orders.AllAsync(o => o.Amount > 10));

        AssertSql(
            """
            SELECT EXISTS (
                SELECT 1
                FROM `orders` AS `o`
                WHERE `o`.`Amount` > 10BD)
            """,
            """
            SELECT NOT EXISTS (
                SELECT 1
                FROM `orders` AS `o`
                WHERE `o`.`Amount` <= 10BD)
            """);
    }

    [Fact]
    public async Task Contains_OnParameterList_ExpandsToParameters()
    {
        await using var context = CreateContext();
        var ids = new List<int> { 1, 2, 3 };

        await context.Orders.Where(o => ids.Contains(o.Id)).ToListAsync();

        AssertSql(
            $"""
            SELECT {OrderColumns}
            FROM `orders` AS `o`
            WHERE `o`.`Id` IN (:ids1, :ids2, :ids3)
            """);
        Assert.Equal([1, 2, 3], Assert.Single(Databricks.Statements).Parameters.Select(p => p.Value));
    }

    [Fact]
    public async Task Contains_OnInlineArray_UsesLiterals()
    {
        await using var context = CreateContext();

        await context.Orders.Where(o => new[] { 1, 2 }.Contains(o.Id)).ToListAsync();

        AssertSql(
            $"""
            SELECT {OrderColumns}
            FROM `orders` AS `o`
            WHERE `o`.`Id` IN (1, 2)
            """);
    }

    [Fact]
    public async Task NullableEquality_ComparesNullsAsEqual()
    {
        await using var context = CreateContext();

        await context.Customers.Where(c => c.City == c.Address.PostCode).ToListAsync();

        AssertSql(
            $"""
            SELECT {CustomerColumns}
            FROM `customers` AS `c`
            WHERE `c`.`City` = `c`.`Address_PostCode` OR (`c`.`City` IS NULL AND `c`.`Address_PostCode` IS NULL)
            """);
    }

    // ---- aggregates ----

    [Fact]
    public async Task Count_ReadsBigintAsInt()
    {
        Databricks.Responder = _ => FakeResult.Table([("count(1)", "BIGINT")], [42L]);
        await using var context = CreateContext();

        Assert.Equal(42, await context.Orders.CountAsync());
        Assert.Equal(42L, await context.Orders.LongCountAsync());

        AssertSql(
            """
            SELECT COUNT(*)
            FROM `orders` AS `o`
            """,
            """
            SELECT COUNT(*)
            FROM `orders` AS `o`
            """);
    }

    [Fact]
    public async Task Sum_And_Average()
    {
        Databricks.Responder = statement => statement.Sql.Contains("AVG", StringComparison.Ordinal)
            ? FakeResult.Table([("avg", "DOUBLE")], [2.5d])
            : FakeResult.Table([("sum", "BIGINT")], [10L]);
        await using var context = CreateContext();

        Assert.Equal(10, await context.Orders.SumAsync(o => o.Quantity));
        Assert.Equal(2.5d, await context.Orders.AverageAsync(o => o.Quantity));

        AssertSql(
            """
            SELECT COALESCE(SUM(`o`.`Quantity`), 0)
            FROM `orders` AS `o`
            """,
            """
            SELECT AVG(CAST(`o`.`Quantity` AS DOUBLE))
            FROM `orders` AS `o`
            """);
    }

    [Fact]
    public async Task GroupBy_WithAggregates_AndHaving()
    {
        await using var context = CreateContext();

        await context.Orders
            .GroupBy(o => o.Status)
            .Where(g => g.Count() > 2)
            .Select(g => new { g.Key, Count = g.Count(), Total = g.Sum(o => o.Amount) })
            .ToListAsync();

        AssertSql(
            """
            SELECT `o`.`Status` AS `Key`, COUNT(*) AS `Count`, COALESCE(SUM(`o`.`Amount`), 0BD) AS `Total`
            FROM `orders` AS `o`
            GROUP BY `o`.`Status`
            HAVING COUNT(*) > 2
            """);
    }

    [Fact]
    public async Task GroupBy_FirstPerGroup_UsesRowNumber()
    {
        await using var context = CreateContext();

        await context.Orders.GroupBy(o => o.CustomerId).Select(g => g.OrderBy(o => o.Amount).First()).ToListAsync();

        AssertSql(
            """
            SELECT `o3`.`Id`, `o3`.`Amount`, `o3`.`CustomerId`, `o3`.`OrderDate`, `o3`.`Quantity`, `o3`.`Status`
            FROM (
                SELECT `o`.`CustomerId`
                FROM `orders` AS `o`
                GROUP BY `o`.`CustomerId`
            ) AS `o1`
            LEFT JOIN (
                SELECT `o2`.`Id`, `o2`.`Amount`, `o2`.`CustomerId`, `o2`.`OrderDate`, `o2`.`Quantity`, `o2`.`Status`
                FROM (
                    SELECT `o0`.`Id`, `o0`.`Amount`, `o0`.`CustomerId`, `o0`.`OrderDate`, `o0`.`Quantity`, `o0`.`Status`, ROW_NUMBER() OVER(PARTITION BY `o0`.`CustomerId` ORDER BY `o0`.`Amount`) AS `row`
                    FROM `orders` AS `o0`
                ) AS `o2`
                WHERE `o2`.`row` <= 1
            ) AS `o3` ON `o1`.`CustomerId` = `o3`.`CustomerId`
            """);
    }

    // ---- joins and navigations ----

    [Fact]
    public async Task Join_And_NavigationFilter()
    {
        await using var context = CreateContext();

        await context.Orders.Join(context.Customers, o => o.CustomerId, c => c.Id, (o, c) => new { o.Id, c.Name }).ToListAsync();
        await context.Orders.Where(o => o.Customer!.City == "Oslo").Select(o => o.Id).ToListAsync();

        AssertSql(
            """
            SELECT `o`.`Id`, `c`.`Name`
            FROM `orders` AS `o`
            INNER JOIN `customers` AS `c` ON `o`.`CustomerId` = `c`.`Id`
            """,
            """
            SELECT `o`.`Id`
            FROM `orders` AS `o`
            INNER JOIN `customers` AS `c` ON `o`.`CustomerId` = `c`.`Id`
            WHERE `c`.`City` = 'Oslo'
            """);
    }

    [Fact]
    public async Task LeftJoin_ViaGroupJoin()
    {
        await using var context = CreateContext();

        await (from c in context.Customers
               join o in context.Orders on c.Id equals o.CustomerId into orders
               from o in orders.DefaultIfEmpty()
               select new { c.Name, Id = (int?)o!.Id }).ToListAsync();

        AssertSql(
            """
            SELECT `c`.`Name`, `o`.`Id`
            FROM `customers` AS `c`
            LEFT JOIN `orders` AS `o` ON `c`.`Id` = `o`.`CustomerId`
            """);
    }

    [Fact]
    public async Task Include_SingleQuery_And_SplitQuery()
    {
        await using var context = CreateContext();

        await context.Customers.Include(c => c.Orders).ToListAsync();
        await context.Customers.Include(c => c.Orders).AsSplitQuery().ToListAsync();

        AssertSql(
            $"""
            SELECT {CustomerColumns}, {OrderColumns}
            FROM `customers` AS `c`
            LEFT JOIN `orders` AS `o` ON `c`.`Id` = `o`.`CustomerId`
            ORDER BY `c`.`Id`
            """,
            $"""
            SELECT {CustomerColumns}
            FROM `customers` AS `c`
            ORDER BY `c`.`Id`
            """);
    }

    [Fact]
    public async Task SelectMany_WithTakePerParent_UsesRowNumber()
    {
        await using var context = CreateContext();

        await context.Customers.SelectMany(c => c.Orders.OrderBy(o => o.Id).Take(2)).ToListAsync();

        AssertSql(
            """
            SELECT `o1`.`Id`, `o1`.`Amount`, `o1`.`CustomerId`, `o1`.`OrderDate`, `o1`.`Quantity`, `o1`.`Status`
            FROM `customers` AS `c`
            INNER JOIN (
                SELECT `o0`.`Id`, `o0`.`Amount`, `o0`.`CustomerId`, `o0`.`OrderDate`, `o0`.`Quantity`, `o0`.`Status`
                FROM (
                    SELECT `o`.`Id`, `o`.`Amount`, `o`.`CustomerId`, `o`.`OrderDate`, `o`.`Quantity`, `o`.`Status`, ROW_NUMBER() OVER(PARTITION BY `o`.`CustomerId` ORDER BY `o`.`Id`) AS `row`
                    FROM `orders` AS `o`
                ) AS `o0`
                WHERE `o0`.`row` <= 2
            ) AS `o1` ON `c`.`Id` = `o1`.`CustomerId`
            """);
    }

    [Fact]
    public async Task CrossApply_BecomesCrossJoinLateral()
    {
        await using var context = CreateContext();

        await context.Customers
            .SelectMany(c => context.Orders.Where(o => o.Amount > c.Id).OrderBy(o => o.Id).Take(2), (c, o) => new { c.Name, o.Id })
            .ToListAsync();

        AssertSql(
            """
            SELECT `c`.`Name`, `o0`.`Id`
            FROM `customers` AS `c`
            CROSS JOIN LATERAL (
                SELECT `o`.`Id`
                FROM `orders` AS `o`
                WHERE `o`.`Amount` > CAST(`c`.`Id` AS DECIMAL(18,2))
                ORDER BY `o`.`Id`
                LIMIT 2
            ) AS `o0`
            """);
    }

    [Fact]
    public async Task OuterApply_BecomesLeftJoinLateral()
    {
        await using var context = CreateContext();

        await context.Customers
            .SelectMany(
                c => context.Orders.Where(o => o.Amount > c.Id).OrderBy(o => o.Id).Take(2).DefaultIfEmpty(),
                (c, o) => new { c.Name, Id = (int?)o!.Id })
            .ToListAsync();

        AssertSql(
            """
            SELECT `c`.`Name`, `o0`.`Id`
            FROM `customers` AS `c`
            LEFT JOIN LATERAL (
                SELECT `o`.`Id`
                FROM `orders` AS `o`
                WHERE `o`.`Amount` > CAST(`c`.`Id` AS DECIMAL(18,2))
                ORDER BY `o`.`Id`
                LIMIT 2
            ) AS `o0` ON TRUE
            """);
    }

    [Fact]
    public async Task CorrelatedSubqueries_InProjectionAndFilter()
    {
        await using var context = CreateContext();

        await context.Customers.Select(c => new { c.Name, Count = c.Orders.Count() }).ToListAsync();
        await context.Customers.Where(c => c.Orders.Any(o => o.Amount > 100)).Select(c => c.Id).ToListAsync();

        AssertSql(
            """
            SELECT `c`.`Name`, (
                SELECT COUNT(*)
                FROM `orders` AS `o`
                WHERE `c`.`Id` = `o`.`CustomerId`) AS `Count`
            FROM `customers` AS `c`
            """,
            """
            SELECT `c`.`Id`
            FROM `customers` AS `c`
            WHERE EXISTS (
                SELECT 1
                FROM `orders` AS `o`
                WHERE `c`.`Id` = `o`.`CustomerId` AND `o`.`Amount` > 100BD)
            """);
    }

    // ---- set operations and distinct ----

    [Fact]
    public async Task SetOperations()
    {
        await using var context = CreateContext();

        await context.Customers.Select(c => c.Name).Union(context.Customers.Select(c => c.City!)).ToListAsync();
        await context.Customers.Select(c => c.Name).Concat(context.Customers.Select(c => c.City!)).ToListAsync();
        await context.Customers.Select(c => c.Name).Except(context.Customers.Select(c => c.City!)).ToListAsync();
        await context.Customers.Select(c => c.Name).Intersect(context.Customers.Select(c => c.City!)).ToListAsync();
        await context.Orders.Select(o => o.Status).Distinct().ToListAsync();

        string SetOperation(string op) =>
            $"""
            SELECT `c`.`Name`
            FROM `customers` AS `c`
            {op}
            SELECT `c0`.`City` AS `Name`
            FROM `customers` AS `c0`
            """;

        AssertSql(
            SetOperation("UNION"),
            SetOperation("UNION ALL"),
            SetOperation("EXCEPT"),
            SetOperation("INTERSECT"),
            """
            SELECT DISTINCT `o`.`Status`
            FROM `orders` AS `o`
            """);
    }

    // ---- operators and expressions ----

    [Fact]
    public async Task StringConcatenation_UsesPipes()
    {
        await using var context = CreateContext();

        await context.Customers.Select(c => c.Name + " - " + c.City).ToListAsync();

        AssertSql(
            """
            SELECT (`c`.`Name` || ' - ') || COALESCE(`c`.`City`, '')
            FROM `customers` AS `c`
            """);
    }

    [Fact]
    public async Task IntegerDivision_UsesDiv_AndDecimalDivisionDoesNot()
    {
        await using var context = CreateContext();

        await context.Orders
            .Select(o => new { A = o.Quantity / 2, B = (o.Quantity + 1) / o.Id, C = (long)o.Quantity / 3L, D = o.Amount / 2 })
            .ToListAsync();

        AssertSql(
            """
            SELECT CAST(`o`.`Quantity` DIV 2 AS INT) AS `A`, CAST((`o`.`Quantity` + 1) DIV `o`.`Id` AS INT) AS `B`, CAST(`o`.`Quantity` AS BIGINT) DIV 3L AS `C`, `o`.`Amount` / 2BD AS `D`
            FROM `orders` AS `o`
            """);
    }

    [Fact]
    public async Task IntegerDivision_InsideLargerExpression_KeepsPrecedence()
    {
        await using var context = CreateContext();

        await context.Orders.Where(o => o.Quantity / 2 + 1 > 3).Select(o => o.Id).ToListAsync();

        AssertSql(
            """
            SELECT `o`.`Id`
            FROM `orders` AS `o`
            WHERE ((CAST(`o`.`Quantity` DIV 2 AS INT)) + 1) > 3
            """);
    }

    [Fact]
    public async Task Modulo_Conditional_Coalesce_And_Cast()
    {
        await using var context = CreateContext();

        await context.Orders.Where(o => o.Quantity % 2 == 0).Select(o => o.Amount > 100 ? "big" : "small").ToListAsync();
        await context.Customers.Select(c => c.City ?? "unknown").ToListAsync();
        await context.Orders.Select(o => (double)o.Quantity).ToListAsync();

        AssertSql(
            """
            SELECT CASE
                WHEN `o`.`Amount` > 100BD THEN 'big'
                ELSE 'small'
            END
            FROM `orders` AS `o`
            WHERE (`o`.`Quantity` % 2) = 0
            """,
            """
            SELECT COALESCE(`c`.`City`, 'unknown')
            FROM `customers` AS `c`
            """,
            """
            SELECT CAST(`o`.`Quantity` AS DOUBLE)
            FROM `orders` AS `o`
            """);
    }

    [Fact]
    public async Task BooleanExpressions_AreNative()
    {
        await using var context = CreateContext();

        await context.Orders.Select(o => new { Big = o.Amount > 100, o.Customer!.IsActive }).ToListAsync();

        AssertSql(
            """
            SELECT `o`.`Amount` > 100BD AS `Big`, `c`.`IsActive`
            FROM `orders` AS `o`
            INNER JOIN `customers` AS `c` ON `o`.`CustomerId` = `c`.`Id`
            """);
    }
}
