using System.Linq.Expressions;
using System.Text.RegularExpressions;
using BricksNet.EntityFrameworkCore.FunctionalTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BricksNet.EntityFrameworkCore.FunctionalTests;

/// <summary>
/// Each translated .NET member or method is evaluated by Databricks inside a WHERE clause (so it
/// cannot silently run on the client), and must select the same rows as .NET does.
/// </summary>
[Collection(LiveCollection.Name)]
[Trait("Category", "E2E")]
public sealed class LiveTranslationTests(LiveDatabaseFixture fixture)
{
    /// <summary>A predicate evaluated on the server, and its .NET equivalent where the two differ.</summary>
    private sealed record Case(string Name, Expression<Func<Sample, bool>> Server, Func<Sample, bool>? Client = null);

    private static readonly DateTime Cutoff = new(2026, 1, 1);

    private static readonly Case[] Cases =
    [
        // strings
        new("Length", s => s.Text.Length > 10),
        new("ToUpper", s => s.Text.ToUpper() == "MIXED CASE"),
        new("ToLower", s => s.Text.ToLower() == "mixed case"),
        new("Trim", s => s.Text.Trim() == "padded"),
        new("TrimStart", s => s.Text.TrimStart().StartsWith("padded")),
        new("Trim(char)", s => s.Text.Trim('a') == "bcabc"),
        new("Substring(start)", s => s.Text.Substring(6) == "world"),
        new("Substring(start, length)", s => s.Text.Substring(1, 3) == "bca"),
        new("IndexOf", s => s.Text.IndexOf("c") == 2),
        new("IndexOf(from)", s => s.Text.IndexOf("a", 1) == 3),
        new("Replace", s => s.Text.Replace("abc", "x") == "xx"),
        new("Contains", s => s.Text.Contains("quot")),
        new("StartsWith", s => s.Text.StartsWith("it's")),
        new("EndsWith(char)", s => s.Text.EndsWith('d')),
        new("IsNullOrEmpty", s => string.IsNullOrEmpty(s.Note)),
        new("IsNullOrWhiteSpace", s => string.IsNullOrWhiteSpace(s.Note)),
        new("Regex.IsMatch", s => Regex.IsMatch(s.Text, "^[a-z]+$")),
        new("Like", s => EF.Functions.Like(s.Text, "%c%"), s => s.Text.Contains('c')),
        new("ILike", s => EF.Functions.ILike(s.Text, "mixed%"), s => s.Text.StartsWith("mixed", StringComparison.OrdinalIgnoreCase)),
        new("Concat", s => s.Text + "!" == "abcabc!"),

        // math — Round/bround must match .NET's half-to-even default
        new("Round(double)", s => Math.Round(s.Value) == 2),
        new("Round(decimal, 2)", s => Math.Round(s.Amount, 2) == 0m),
        new("Round(AwayFromZero)", s => Math.Round(s.Value, MidpointRounding.AwayFromZero) == 3),
        new("Ceiling", s => Math.Ceiling(s.Value) == 3),
        new("Floor", s => Math.Floor(s.Value) == -3),
        new("Truncate", s => Math.Truncate(s.Value) == -2),
        new("Abs", s => Math.Abs(s.Number) == 3),
        new("Pow", s => Math.Pow(s.Value, 2) > 6),
        new("Sqrt", s => Math.Sqrt(s.Number) > 6),
        new("Log10", s => Math.Log10(s.Value) > 9),
        new("Sign", s => Math.Sign(s.Number) == -1),
        new("Max", s => Math.Max(s.Number, 10) == 10),
        new("Integer division", s => s.Number / 2 == -1),
        new("Modulo", s => s.Number % 2 == 1),

        // dates and times
        new("Year", s => s.At.Year == 2026),
        new("Month", s => s.Day.Month == 2),
        new("Day", s => s.At.Day == 29),
        new("DayOfWeek", s => s.At.DayOfWeek == DayOfWeek.Thursday),
        new("DayOfYear", s => s.Day.DayOfYear == 366),
        new("Hour", s => s.At.Hour == 18),
        new("Date", s => s.At.Date == new DateTime(2026, 3, 4)),
        new("AddDays", s => s.At.AddDays(1).Day == 1),
        new("AddMonths", s => s.At.AddMonths(1).Month == 3),
        new("AddHours(fraction)", s => s.At.AddHours(1.5).Hour == 6),
        new("DateOnly.AddDays", s => s.Day.AddDays(1) == new DateOnly(2024, 3, 1)),
        new("DateOnly.AddYears", s => s.Day.AddYears(1) == new DateOnly(2025, 2, 28)),
        new("DateOnly.FromDateTime", s => DateOnly.FromDateTime(s.At) == s.Day),
        new("DateTimeOffset.Year", s => s.Stamp.Year == 2000),
        new("DateDiffDay", s => EF.Functions.DateDiffDay(Cutoff, s.At) > 100, s => (s.At - Cutoff).TotalDays >= 101),

        // conversions
        new("int.ToString", s => s.Number.ToString() == "-3"),
        new("bool.ToString", s => s.Flag.ToString() == "True"),
        new("Convert.ToDouble", s => Convert.ToDouble(s.Number) > 40),
    ];

    public static TheoryData<string> CaseNames() => new(Cases.Select(c => c.Name));

    [LiveTheory]
    [MemberData(nameof(CaseNames))]
    public async Task Translation_MatchesDotNet(string name)
    {
        var testCase = Cases.Single(c => c.Name == name);
        await using var context = fixture.CreateContext();

        var server = await context.Samples.Where(testCase.Server).Select(s => s.Id).OrderBy(id => id).ToListAsync();
        var client = fixture.Samples.Where(testCase.Client ?? testCase.Server.Compile()).Select(s => s.Id).Order().ToList();

        Assert.Equal(client, server);
    }

    [LiveFact]
    public async Task CurrentTime_IsCloseToTheClientsClock()
    {
        await using var context = fixture.CreateContext();

        var utcNow = await context.Samples.Select(_ => DateTime.UtcNow).FirstAsync();
        var stamp = await context.Samples.Select(_ => DateTimeOffset.UtcNow).FirstAsync();

        Assert.InRange(utcNow, DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow.AddMinutes(5));
        Assert.InRange(stamp, DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(5));
    }
}
