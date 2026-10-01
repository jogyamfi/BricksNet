using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace BricksNet.EntityFrameworkCore.Query.Internal.Translators;

/// <summary>Translates the Databricks-specific <see cref="BricksNetDbFunctionsExtensions"/> methods.</summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class BricksNetDbFunctionsTranslator : IMethodCallTranslator
{
    private static readonly Dictionary<string, string> DateDiffUnits = new(StringComparer.Ordinal)
    {
        [nameof(BricksNetDbFunctionsExtensions.DateDiffYear)] = "YEAR",
        [nameof(BricksNetDbFunctionsExtensions.DateDiffMonth)] = "MONTH",
        [nameof(BricksNetDbFunctionsExtensions.DateDiffDay)] = "DAY",
        [nameof(BricksNetDbFunctionsExtensions.DateDiffHour)] = "HOUR",
        [nameof(BricksNetDbFunctionsExtensions.DateDiffMinute)] = "MINUTE",
        [nameof(BricksNetDbFunctionsExtensions.DateDiffSecond)] = "SECOND",
    };

    private readonly ISqlExpressionFactory _sql;
    private readonly IRelationalTypeMappingSource _typeMappingSource;

    /// <summary>Initialises a new instance.</summary>
    public BricksNetDbFunctionsTranslator(ISqlExpressionFactory sqlExpressionFactory, IRelationalTypeMappingSource typeMappingSource)
    {
        _sql = sqlExpressionFactory;
        _typeMappingSource = typeMappingSource;
    }

    /// <summary>The message thrown when a <see cref="BricksNetDbFunctionsExtensions"/> method runs outside a query.</summary>
    internal static string ClientEvaluationMessage(string method) =>
        $"EF.Functions.{method} can only be used in LINQ queries translated by the BricksNet EF Core provider.";

    /// <inheritdoc />
    public virtual SqlExpression? Translate(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (method.DeclaringType != typeof(BricksNetDbFunctionsExtensions))
        {
            return null;
        }

        if (method.Name == nameof(BricksNetDbFunctionsExtensions.ILike))
        {
            // arguments[0] is the DbFunctions instance.
            var match = arguments[1];
            var stringMapping = match.TypeMapping ?? _typeMappingSource.FindMapping(typeof(string));
            var pattern = _sql.ApplyTypeMapping(arguments[2], stringMapping);
            var escape = arguments.Count > 3 ? _sql.ApplyTypeMapping(arguments[3], stringMapping) : null;
            return _sql.Like(Lower(match, stringMapping), Lower(pattern, stringMapping), escape);
        }

        if (DateDiffUnits.TryGetValue(method.Name, out var unit))
        {
            var difference = _sql.Function(
                "timestampdiff",
                [_sql.Unit(unit), arguments[1], arguments[2]],
                typeof(long),
                _typeMappingSource.FindMapping(typeof(long)));

            return method.ReturnType == typeof(long)
                ? difference
                : _sql.Convert(difference, method.ReturnType, _typeMappingSource.FindMapping(method.ReturnType));
        }

        return null;
    }

    private SqlExpression Lower(SqlExpression value, RelationalTypeMapping? stringMapping) =>
        _sql.Function("lower", [value], typeof(string), stringMapping);
}
