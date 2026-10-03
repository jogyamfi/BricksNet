using BricksNet.EntityFrameworkCore.Query.Internal.Translators;

// Extension methods live in EF's namespace so EF.Functions.* is discoverable next to the built-in ones.
namespace Microsoft.EntityFrameworkCore;

/// <summary>
/// Databricks-specific functions for <see cref="EF.Functions"/>. They are only for use in LINQ
/// queries and throw when called directly.
/// </summary>
public static class BricksNetDbFunctionsExtensions
{
    /// <summary>
    /// Case-insensitive <c>LIKE</c>: <c>lower(matchExpression) LIKE lower(pattern)</c>.
    /// </summary>
    /// <param name="_">The <see cref="DbFunctions"/> instance.</param>
    /// <param name="matchExpression">The value to match.</param>
    /// <param name="pattern">The pattern, with <c>%</c> and <c>_</c> wildcards.</param>
    /// <returns><see langword="true"/> when the value matches.</returns>
    public static bool ILike(this DbFunctions _, string? matchExpression, string? pattern) =>
        throw ClientEvaluation(nameof(ILike));

    /// <summary>
    /// Case-insensitive <c>LIKE</c> with an escape character for literal <c>%</c> and <c>_</c>.
    /// </summary>
    /// <param name="_">The <see cref="DbFunctions"/> instance.</param>
    /// <param name="matchExpression">The value to match.</param>
    /// <param name="pattern">The pattern, with <c>%</c> and <c>_</c> wildcards.</param>
    /// <param name="escapeCharacter">The character that escapes a wildcard in <paramref name="pattern"/>.</param>
    /// <returns><see langword="true"/> when the value matches.</returns>
    public static bool ILike(this DbFunctions _, string? matchExpression, string? pattern, string escapeCharacter) =>
        throw ClientEvaluation(nameof(ILike));

    /// <summary>Whole years between two timestamps: <c>timestampdiff(YEAR, start, end)</c>.</summary>
    public static int DateDiffYear(this DbFunctions _, DateTime start, DateTime end) => throw ClientEvaluation(nameof(DateDiffYear));

    /// <summary>Whole years between two timestamps: <c>timestampdiff(YEAR, start, end)</c>.</summary>
    public static int DateDiffYear(this DbFunctions _, DateTimeOffset start, DateTimeOffset end) => throw ClientEvaluation(nameof(DateDiffYear));

    /// <summary>Whole years between two dates: <c>timestampdiff(YEAR, start, end)</c>.</summary>
    public static int DateDiffYear(this DbFunctions _, DateOnly start, DateOnly end) => throw ClientEvaluation(nameof(DateDiffYear));

    /// <summary>Whole months between two timestamps: <c>timestampdiff(MONTH, start, end)</c>.</summary>
    public static int DateDiffMonth(this DbFunctions _, DateTime start, DateTime end) => throw ClientEvaluation(nameof(DateDiffMonth));

    /// <summary>Whole months between two timestamps: <c>timestampdiff(MONTH, start, end)</c>.</summary>
    public static int DateDiffMonth(this DbFunctions _, DateTimeOffset start, DateTimeOffset end) => throw ClientEvaluation(nameof(DateDiffMonth));

    /// <summary>Whole months between two dates: <c>timestampdiff(MONTH, start, end)</c>.</summary>
    public static int DateDiffMonth(this DbFunctions _, DateOnly start, DateOnly end) => throw ClientEvaluation(nameof(DateDiffMonth));

    /// <summary>Whole days between two timestamps: <c>timestampdiff(DAY, start, end)</c>.</summary>
    public static int DateDiffDay(this DbFunctions _, DateTime start, DateTime end) => throw ClientEvaluation(nameof(DateDiffDay));

    /// <summary>Whole days between two timestamps: <c>timestampdiff(DAY, start, end)</c>.</summary>
    public static int DateDiffDay(this DbFunctions _, DateTimeOffset start, DateTimeOffset end) => throw ClientEvaluation(nameof(DateDiffDay));

    /// <summary>Days between two dates: <c>timestampdiff(DAY, start, end)</c>.</summary>
    public static int DateDiffDay(this DbFunctions _, DateOnly start, DateOnly end) => throw ClientEvaluation(nameof(DateDiffDay));

    /// <summary>Whole hours between two timestamps: <c>timestampdiff(HOUR, start, end)</c>.</summary>
    public static long DateDiffHour(this DbFunctions _, DateTime start, DateTime end) => throw ClientEvaluation(nameof(DateDiffHour));

    /// <summary>Whole hours between two timestamps: <c>timestampdiff(HOUR, start, end)</c>.</summary>
    public static long DateDiffHour(this DbFunctions _, DateTimeOffset start, DateTimeOffset end) => throw ClientEvaluation(nameof(DateDiffHour));

    /// <summary>Whole minutes between two timestamps: <c>timestampdiff(MINUTE, start, end)</c>.</summary>
    public static long DateDiffMinute(this DbFunctions _, DateTime start, DateTime end) => throw ClientEvaluation(nameof(DateDiffMinute));

    /// <summary>Whole minutes between two timestamps: <c>timestampdiff(MINUTE, start, end)</c>.</summary>
    public static long DateDiffMinute(this DbFunctions _, DateTimeOffset start, DateTimeOffset end) => throw ClientEvaluation(nameof(DateDiffMinute));

    /// <summary>Whole seconds between two timestamps: <c>timestampdiff(SECOND, start, end)</c>.</summary>
    public static long DateDiffSecond(this DbFunctions _, DateTime start, DateTime end) => throw ClientEvaluation(nameof(DateDiffSecond));

    /// <summary>Whole seconds between two timestamps: <c>timestampdiff(SECOND, start, end)</c>.</summary>
    public static long DateDiffSecond(this DbFunctions _, DateTimeOffset start, DateTimeOffset end) => throw ClientEvaluation(nameof(DateDiffSecond));

    private static InvalidOperationException ClientEvaluation(string method) =>
        new(BricksNetDbFunctionsTranslator.ClientEvaluationMessage(method));
}
