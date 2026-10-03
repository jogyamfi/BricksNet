using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace BricksNet.EntityFrameworkCore.Query.Internal.Translators;

/// <summary>
/// Translates <see cref="string"/> methods to Databricks string functions. Positions are
/// converted between .NET's 0-based and Databricks' 1-based indexing.
/// </summary>
/// <remarks>
/// Databricks <c>trim</c> removes spaces only, while .NET <c>Trim()</c> removes all whitespace.
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class BricksNetStringMethodTranslator : IMethodCallTranslator
{
    private static readonly MethodInfo IndexOfString = typeof(string).GetRuntimeMethod(nameof(string.IndexOf), [typeof(string)])!;
    private static readonly MethodInfo IndexOfChar = typeof(string).GetRuntimeMethod(nameof(string.IndexOf), [typeof(char)])!;
    private static readonly MethodInfo IndexOfStringFrom = typeof(string).GetRuntimeMethod(nameof(string.IndexOf), [typeof(string), typeof(int)])!;
    private static readonly MethodInfo ReplaceString = typeof(string).GetRuntimeMethod(nameof(string.Replace), [typeof(string), typeof(string)])!;
    private static readonly MethodInfo ReplaceChar = typeof(string).GetRuntimeMethod(nameof(string.Replace), [typeof(char), typeof(char)])!;
    private static readonly MethodInfo ToLower = typeof(string).GetRuntimeMethod(nameof(string.ToLower), Type.EmptyTypes)!;
    private static readonly MethodInfo ToUpper = typeof(string).GetRuntimeMethod(nameof(string.ToUpper), Type.EmptyTypes)!;
    private static readonly MethodInfo SubstringFrom = typeof(string).GetRuntimeMethod(nameof(string.Substring), [typeof(int)])!;
    private static readonly MethodInfo SubstringFromLength = typeof(string).GetRuntimeMethod(nameof(string.Substring), [typeof(int), typeof(int)])!;
    private static readonly MethodInfo Trim = typeof(string).GetRuntimeMethod(nameof(string.Trim), Type.EmptyTypes)!;
    private static readonly MethodInfo TrimChar = typeof(string).GetRuntimeMethod(nameof(string.Trim), [typeof(char)])!;
    private static readonly MethodInfo TrimChars = typeof(string).GetRuntimeMethod(nameof(string.Trim), [typeof(char[])])!;
    private static readonly MethodInfo TrimStart = typeof(string).GetRuntimeMethod(nameof(string.TrimStart), Type.EmptyTypes)!;
    private static readonly MethodInfo TrimEnd = typeof(string).GetRuntimeMethod(nameof(string.TrimEnd), Type.EmptyTypes)!;
    private static readonly MethodInfo ContainsString = typeof(string).GetRuntimeMethod(nameof(string.Contains), [typeof(string)])!;
    private static readonly MethodInfo ContainsChar = typeof(string).GetRuntimeMethod(nameof(string.Contains), [typeof(char)])!;
    private static readonly MethodInfo StartsWithString = typeof(string).GetRuntimeMethod(nameof(string.StartsWith), [typeof(string)])!;
    private static readonly MethodInfo StartsWithChar = typeof(string).GetRuntimeMethod(nameof(string.StartsWith), [typeof(char)])!;
    private static readonly MethodInfo EndsWithString = typeof(string).GetRuntimeMethod(nameof(string.EndsWith), [typeof(string)])!;
    private static readonly MethodInfo EndsWithChar = typeof(string).GetRuntimeMethod(nameof(string.EndsWith), [typeof(char)])!;
    private static readonly MethodInfo IsNullOrEmpty = typeof(string).GetRuntimeMethod(nameof(string.IsNullOrEmpty), [typeof(string)])!;
    private static readonly MethodInfo IsNullOrWhiteSpace = typeof(string).GetRuntimeMethod(nameof(string.IsNullOrWhiteSpace), [typeof(string)])!;

    private readonly ISqlExpressionFactory _sql;
    private readonly RelationalTypeMapping _intMapping;
    private readonly RelationalTypeMapping _boolMapping;

    /// <summary>Initialises a new instance.</summary>
    public BricksNetStringMethodTranslator(ISqlExpressionFactory sqlExpressionFactory, IRelationalTypeMappingSource typeMappingSource)
    {
        _sql = sqlExpressionFactory;
        _intMapping = typeMappingSource.FindMapping(typeof(int))!;
        _boolMapping = typeMappingSource.FindMapping(typeof(bool))!;
    }

    /// <inheritdoc />
    public virtual SqlExpression? Translate(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (instance is null)
        {
            return TranslateStatic(method, arguments);
        }

        if (method.DeclaringType != typeof(string))
        {
            return null;
        }

        var stringMapping = instance.TypeMapping;

        if (method == IndexOfString || method == IndexOfChar)
        {
            var argument = AsString(arguments[0], stringMapping);
            return _sql.Subtract(_sql.Function("instr", [instance, argument], typeof(int), _intMapping), _sql.Constant(1, _intMapping));
        }

        if (method == IndexOfStringFrom)
        {
            var argument = AsString(arguments[0], stringMapping);
            var position = OneBased(arguments[1]);
            return _sql.Subtract(_sql.Function("locate", [argument, instance, position], typeof(int), _intMapping), _sql.Constant(1, _intMapping));
        }

        if (method == ReplaceString || method == ReplaceChar)
        {
            return _sql.Function(
                "replace",
                [instance, AsString(arguments[0], stringMapping), AsString(arguments[1], stringMapping)],
                typeof(string),
                stringMapping);
        }

        if (method == ToLower || method == ToUpper)
        {
            return _sql.Function(method == ToLower ? "lower" : "upper", [instance], typeof(string), stringMapping);
        }

        if (method == SubstringFrom)
        {
            return _sql.Function("substring", [instance, OneBased(arguments[0])], typeof(string), stringMapping);
        }

        if (method == SubstringFromLength)
        {
            return _sql.Function("substring", [instance, OneBased(arguments[0]), arguments[1]], typeof(string), stringMapping);
        }

        if (method == Trim || method == TrimStart || method == TrimEnd)
        {
            var name = method == Trim ? "trim" : method == TrimStart ? "ltrim" : "rtrim";
            return _sql.Function(name, [instance], typeof(string), stringMapping);
        }

        if ((method == TrimChar || method == TrimChars) && arguments[0] is SqlConstantExpression { Value: { } trimValue })
        {
            var characters = trimValue is char c ? c.ToString() : new string((char[])trimValue);
            return characters.Length == 0
                ? _sql.Function("trim", [instance], typeof(string), stringMapping)
                : _sql.Function("btrim", [instance, _sql.Constant(characters, stringMapping)], typeof(string), stringMapping);
        }

        var predicate = method == ContainsString || method == ContainsChar ? "contains"
            : method == StartsWithString || method == StartsWithChar ? "startswith"
            : method == EndsWithString || method == EndsWithChar ? "endswith"
            : null;
        if (predicate is not null)
        {
            return _sql.Function(predicate, [instance, AsString(arguments[0], stringMapping)], typeof(bool), _boolMapping);
        }

        return null;
    }

    private SqlExpression? TranslateStatic(MethodInfo method, IReadOnlyList<SqlExpression> arguments)
    {
        if (method == IsNullOrEmpty)
        {
            var value = arguments[0];
            return _sql.OrElse(_sql.IsNull(value), _sql.Equal(value, _sql.Constant(string.Empty, value.TypeMapping)));
        }

        if (method == IsNullOrWhiteSpace)
        {
            // Matches .NET whitespace (spaces, tabs, line breaks), which Databricks trim() does not strip.
            var value = arguments[0];
            return _sql.OrElse(
                _sql.IsNull(value),
                _sql.Function("rlike", [value, _sql.Constant(@"^\s*$", value.TypeMapping)], typeof(bool), _boolMapping));
        }

        return null;
    }

    // A char argument is stored as a one-character STRING: a constant becomes a string literal, and
    // anything else keeps the char mapping, whose converter sends it as a string.
    private SqlExpression AsString(SqlExpression argument, RelationalTypeMapping? stringMapping) => argument switch
    {
        SqlConstantExpression { Value: char c } => _sql.Constant(c.ToString(), stringMapping),
        _ when argument.Type == typeof(char) => _sql.ApplyDefaultTypeMapping(argument),
        _ => _sql.ApplyTypeMapping(argument, stringMapping),
    };

    private SqlExpression OneBased(SqlExpression zeroBased) =>
        zeroBased is SqlConstantExpression { Value: int value }
            ? _sql.Constant(value + 1, _intMapping)
            : _sql.Add(zeroBased, _sql.Constant(1, _intMapping));
}
