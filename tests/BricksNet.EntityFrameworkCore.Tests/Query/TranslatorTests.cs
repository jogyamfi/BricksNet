using System.Linq.Expressions;
using System.Text.RegularExpressions;
using BricksNet.EntityFrameworkCore.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BricksNet.EntityFrameworkCore.Query;

/// <summary>SQL baselines for .NET member and method translations to Databricks functions.</summary>
public sealed class TranslatorTests : QueryTestBase
{
    /// <summary>Runs <c>SELECT selector FROM all_types</c> for each selector and asserts the select lists.</summary>
    private async Task AssertProjections(params (Expression<Func<AllTypes, object?>> Selector, string Sql)[] cases)
    {
        await using var context = CreateAllTypesContext();
        foreach (var (selector, _) in cases)
        {
            await context.AllTypes.Select(selector).ToListAsync();
        }

        AssertSql(cases.Select(c => $"SELECT {c.Sql}\nFROM `all_types` AS `a`").ToArray());
    }

    /// <summary>Runs <c>SELECT Id FROM all_types WHERE predicate</c> and asserts the WHERE clauses.</summary>
    private async Task AssertFilters(params (Expression<Func<AllTypes, bool>> Predicate, string Sql)[] cases)
    {
        await using var context = CreateAllTypesContext();
        foreach (var (predicate, _) in cases)
        {
            await context.AllTypes.Where(predicate).Select(e => e.Id).ToListAsync();
        }

        AssertSql(cases.Select(c => $"SELECT `a`.`Id`\nFROM `all_types` AS `a`\nWHERE {c.Sql}").ToArray());
    }

    // ---- strings ----

    [Fact]
    public Task String_CaseLengthAndTrim() => AssertProjections(
        (e => e.Text.Length, "char_length(`a`.`Text`)"),
        (e => e.Text.ToUpper(), "upper(`a`.`Text`)"),
        (e => e.Text.ToLower(), "lower(`a`.`Text`)"),
        (e => e.Text.Trim(), "trim(`a`.`Text`)"),
        (e => e.Text.TrimStart(), "ltrim(`a`.`Text`)"),
        (e => e.Text.TrimEnd(), "rtrim(`a`.`Text`)"),
        (e => e.Text.Trim('x'), "btrim(`a`.`Text`, 'x')"));

    [Fact]
    public Task String_PositionsAreOneBased()
    {
        var start = 2;
        return AssertProjections(
            (e => e.Text.Substring(1), "substring(`a`.`Text`, 2)"),
            (e => e.Text.Substring(start, 3), "substring(`a`.`Text`, :start + 1, 3)"),
            (e => e.Text.IndexOf("b"), "instr(`a`.`Text`, 'b') - 1"),
            (e => e.Text.IndexOf('b'), "instr(`a`.`Text`, 'b') - 1"),
            (e => e.Text.IndexOf("b", start), "locate('b', `a`.`Text`, :start + 1) - 1"));
    }

    [Fact]
    public Task String_SearchAndReplace()
    {
        var fragment = "ab";
        return AssertFilters(
            (e => e.Text.Contains(fragment), "contains(`a`.`Text`, :fragment)"),
            (e => e.Text.StartsWith("ab"), "startswith(`a`.`Text`, 'ab')"),
            (e => e.Text.EndsWith('z'), "endswith(`a`.`Text`, 'z')"),
            (e => e.Text.Replace("a", "b") == "bb", "replace(`a`.`Text`, 'a', 'b') = 'bb'"),
            (e => e.Text.Replace('a', 'b') == "bb", "replace(`a`.`Text`, 'a', 'b') = 'bb'"));
    }

    [Fact]
    public Task String_NullOrEmptyChecks() => AssertFilters(
        (e => string.IsNullOrEmpty(e.Note), "`a`.`Note` IS NULL OR `a`.`Note` = ''"),
        (e => string.IsNullOrWhiteSpace(e.Note), @"`a`.`Note` IS NULL OR rlike(`a`.`Note`, '^\\s*$')"));

    [Fact]
    public Task String_PatternsAndComparison() => AssertFilters(
        (e => EF.Functions.Like(e.Text, "a%"), "`a`.`Text` LIKE 'a%'"),
        (e => EF.Functions.ILike(e.Text, "a%"), "lower(`a`.`Text`) LIKE lower('a%')"),
        (e => Regex.IsMatch(e.Text, "^a.*"), "rlike(`a`.`Text`, '^a.*')"),
        (e => Regex.IsMatch(e.Text, "^a", RegexOptions.IgnoreCase), "rlike(`a`.`Text`, '(?i)^a')"),
        (e => string.Compare(e.Text, "m") > 0, "`a`.`Text` > 'm'"));

    [Fact]
    public async Task String_Join_OverGroup_UsesCollectList()
    {
        await using var context = CreateContext();

        await context.Orders
            .GroupBy(o => o.CustomerId)
            .Select(g => new { g.Key, Statuses = string.Join(", ", g.Select(o => o.Status)) })
            .ToListAsync();
        await context.Orders
            .GroupBy(o => o.CustomerId)
            .Select(g => string.Join("|", g.Where(o => o.Amount > 1).Select(o => o.Status).Distinct()))
            .ToListAsync();

        AssertSql(
            """
            SELECT `o`.`CustomerId` AS `Key`, array_join(collect_list(`o`.`Status`), ', ') AS `Statuses`
            FROM `orders` AS `o`
            GROUP BY `o`.`CustomerId`
            """,
            """
            SELECT array_join(collect_set(CASE
                WHEN `o`.`Amount` > 1BD THEN `o`.`Status`
            END), '|')
            FROM `orders` AS `o`
            GROUP BY `o`.`CustomerId`
            """);
    }

    [Fact]
    public async Task String_Join_OverOrderedGroup_JoinsOnTheClient()
    {
        await using var context = CreateContext();

        // collect_list has no guaranteed order, so EF fetches the ordered elements and joins them itself.
        await context.Orders
            .GroupBy(o => o.CustomerId)
            .Select(g => string.Join(", ", g.OrderBy(o => o.Id).Select(o => o.Status)))
            .ToListAsync();

        AssertSql(
            """
            SELECT `o1`.`CustomerId`, `o0`.`Status`, `o0`.`Id`
            FROM (
                SELECT `o`.`CustomerId`
                FROM `orders` AS `o`
                GROUP BY `o`.`CustomerId`
            ) AS `o1`
            LEFT JOIN `orders` AS `o0` ON `o1`.`CustomerId` = `o0`.`CustomerId`
            ORDER BY `o1`.`CustomerId`, `o0`.`Id`
            """);
    }

    // ---- math ----

    [Fact]
    public Task Math_Rounding_MatchesDotNetSemantics() => AssertProjections(
        (e => Math.Round(e.Double), "bround(`a`.`Double`)"),
        (e => Math.Round(e.Decimal, 2), "bround(`a`.`Decimal`, 2)"),
        (e => Math.Round(e.Double, MidpointRounding.AwayFromZero), "round(`a`.`Double`)"),
        (e => Math.Ceiling(e.Double), "CAST(ceil(`a`.`Double`) AS DOUBLE)"),
        (e => Math.Floor(e.Double), "CAST(floor(`a`.`Double`) AS DOUBLE)"),
        (e => Math.Ceiling(e.Decimal), "ceil(`a`.`Decimal`)"),
        (e => Math.Truncate(e.Double), "CAST(CASE\n    WHEN `a`.`Double` < 0D THEN ceil(`a`.`Double`)\n    ELSE floor(`a`.`Double`)\nEND AS DOUBLE)"));

    [Fact]
    public Task Math_Functions() => AssertProjections(
        (e => Math.Abs(e.Short), "abs(`a`.`Short`)"),
        (e => Math.Pow(e.Double, 2), "pow(`a`.`Double`, 2D)"),
        (e => Math.Sqrt(e.Double), "sqrt(`a`.`Double`)"),
        (e => Math.Log(e.Double), "ln(`a`.`Double`)"),
        (e => Math.Log(e.Double, 2), "log(2D, `a`.`Double`)"),
        (e => Math.Log10(e.Double), "log10(`a`.`Double`)"),
        (e => Math.Sign(e.Double), "CAST(sign(`a`.`Double`) AS INT)"),
        (e => MathF.Sqrt(e.Float), "CAST(sqrt(`a`.`Float`) AS FLOAT)"));

    [Fact]
    public Task Math_MaxMin_AndGreatestLeast() => AssertFilters(
        (e => Math.Max(e.Long, e.Id) > 5, "greatest(`a`.`Long`, CAST(`a`.`Id` AS BIGINT)) > 5L"),
        (e => Math.Min(e.Double, 1.5) < 1, "least(`a`.`Double`, 1.5D) < 1D"),
        (e => EF.Functions.Greatest(e.Id, e.Short, 3) > 5, "greatest(`a`.`Id`, CAST(`a`.`Short` AS INT), 3) > 5"));

    // ---- dates and times ----

    [Fact]
    public Task DateTime_Parts() => AssertProjections(
        (e => e.DateTime.Year, "year(`a`.`DateTime`)"),
        (e => e.DateTimeOffset.Month, "month(`a`.`DateTimeOffset`)"),
        (e => e.Date.Day, "day(`a`.`Date`)"),
        (e => e.Date.DayOfYear, "dayofyear(`a`.`Date`)"),
        (e => e.DateTime.DayOfWeek, "CAST(dayofweek(`a`.`DateTime`) - 1 AS INT)"),
        (e => e.DateTime.Date, "CAST(CAST(`a`.`DateTime` AS DATE) AS TIMESTAMP_NTZ)"),
        (e => DateOnly.FromDateTime(e.DateTime), "CAST(`a`.`DateTime` AS DATE)"));

    [Fact]
    public Task DateTime_CurrentTime() => AssertFilters(
        (e => e.DateTime < DateTime.Now, "`a`.`DateTime` < localtimestamp()"),
        (e => e.DateTime < DateTime.UtcNow, "`a`.`DateTime` < convert_timezone('UTC', localtimestamp())"),
        (e => e.DateTime < DateTime.Today, "`a`.`DateTime` < CAST(current_date() AS TIMESTAMP_NTZ)"),
        (e => e.DateTimeOffset < DateTimeOffset.UtcNow, "`a`.`DateTimeOffset` < current_timestamp()"));

    [Fact]
    public Task DateTime_Arithmetic()
    {
        var hours = 1.5;
        return AssertProjections(
            (e => e.DateTime.AddDays(7), "timestampadd(DAY, 7L, `a`.`DateTime`)"),
            (e => e.DateTime.AddMonths(1), "timestampadd(MONTH, 1, `a`.`DateTime`)"),
            (e => e.DateTime.AddHours(hours), "timestampadd(MICROSECOND, CAST(round(:hours * 3600000D) * 1000D AS BIGINT), `a`.`DateTime`)"),
            (e => e.DateTimeOffset.AddMinutes(30), "timestampadd(MINUTE, 30L, `a`.`DateTimeOffset`)"),
            (e => e.Date.AddDays(3), "date_add(`a`.`Date`, 3)"),
            (e => e.Date.AddMonths(2), "add_months(`a`.`Date`, 2)"),
            (e => e.Date.AddYears(1), "add_months(`a`.`Date`, 1 * 12)"));
    }

    [Fact]
    public Task DateDiff_Functions()
    {
        var cutoff = new DateTime(2026, 1, 1);
        return AssertProjections(
            (e => EF.Functions.DateDiffDay(cutoff, e.DateTime), "CAST(timestampdiff(DAY, :cutoff, `a`.`DateTime`) AS INT)"),
            (e => EF.Functions.DateDiffMonth(e.Date, e.Date), "CAST(timestampdiff(MONTH, `a`.`Date`, `a`.`Date`) AS INT)"),
            (e => EF.Functions.DateDiffHour(e.DateTimeOffset, e.DateTimeOffset), "timestampdiff(HOUR, `a`.`DateTimeOffset`, `a`.`DateTimeOffset`)"));
    }

    [Fact]
    public void DbFunctions_ThrowOutsideQueries()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => EF.Functions.ILike("a", "a"));

        Assert.Contains("only be used in LINQ queries", ex.Message, StringComparison.Ordinal);
    }

    // ---- conversions and ToString ----

    [Fact]
    public Task Conversions() => AssertProjections(
        (e => Convert.ToInt32(e.Text), "CAST(`a`.`Text` AS INT)"),
        (e => Convert.ToString(e.Double), "CAST(`a`.`Double` AS STRING)"),
        (e => Convert.ToDecimal(e.Double), "CAST(`a`.`Double` AS DECIMAL(18,2))"),
        (e => e.Id.ToString(), "CAST(`a`.`Id` AS STRING)"),
        (e => e.Bool.ToString(), "CASE\n    WHEN `a`.`Bool` THEN 'True'\n    ELSE 'False'\nEND"),
        (e => e.NullableInt.ToString(), "COALESCE(CAST(`a`.`NullableInt` AS STRING), '')"),
        (e => e.Guid.ToString(), "CAST(`a`.`Guid` AS STRING)"));

    [Fact]
    public Task ByteArray_Length() => AssertFilters((e => e.Bytes.Length > 2, "length(`a`.`Bytes`) > 2"));

    [Fact]
    public Task Guid_NewGuid_UsesUuid() => AssertProjections((e => Guid.NewGuid(), "uuid()"));
}
