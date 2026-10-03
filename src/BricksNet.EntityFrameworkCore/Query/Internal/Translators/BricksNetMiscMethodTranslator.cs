using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace BricksNet.EntityFrameworkCore.Query.Internal.Translators;

/// <summary>
/// Translates <see cref="Guid.NewGuid"/> to <c>uuid()</c> and <see cref="Regex.IsMatch(string, string)"/>
/// to <c>rlike</c>.
/// </summary>
/// <remarks>
/// Databricks evaluates patterns with Java regular expressions, which differ from .NET's in some
/// constructs. <see cref="RegexOptions.IgnoreCase"/> is supported for constant patterns only.
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class BricksNetMiscMethodTranslator : IMethodCallTranslator
{
    private static readonly MethodInfo NewGuid = typeof(Guid).GetRuntimeMethod(nameof(Guid.NewGuid), Type.EmptyTypes)!;
    private static readonly MethodInfo IsMatch = typeof(Regex).GetRuntimeMethod(nameof(Regex.IsMatch), [typeof(string), typeof(string)])!;
    private static readonly MethodInfo IsMatchWithOptions =
        typeof(Regex).GetRuntimeMethod(nameof(Regex.IsMatch), [typeof(string), typeof(string), typeof(RegexOptions)])!;

    private readonly ISqlExpressionFactory _sql;
    private readonly IRelationalTypeMappingSource _typeMappingSource;

    /// <summary>Initialises a new instance.</summary>
    public BricksNetMiscMethodTranslator(ISqlExpressionFactory sqlExpressionFactory, IRelationalTypeMappingSource typeMappingSource)
    {
        _sql = sqlExpressionFactory;
        _typeMappingSource = typeMappingSource;
    }

    /// <inheritdoc />
    public virtual SqlExpression? Translate(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (method == NewGuid)
        {
            return _sql.Function("uuid", [], nullable: false, [], typeof(Guid), _typeMappingSource.FindMapping(typeof(Guid)));
        }

        if (method != IsMatch && method != IsMatchWithOptions)
        {
            return null;
        }

        var input = arguments[0];
        var pattern = arguments[1];
        if (method == IsMatchWithOptions)
        {
            if (arguments[2] is not SqlConstantExpression { Value: RegexOptions options })
            {
                return null;
            }

            if (options == RegexOptions.IgnoreCase && pattern is SqlConstantExpression { Value: string text })
            {
                pattern = _sql.Constant("(?i)" + text, pattern.TypeMapping);
            }
            else if (options != RegexOptions.None)
            {
                return null;
            }
        }

        var stringMapping = input.TypeMapping ?? _typeMappingSource.FindMapping(typeof(string));
        return _sql.Function(
            "rlike",
            [input, _sql.ApplyTypeMapping(pattern, stringMapping)],
            typeof(bool),
            _typeMappingSource.FindMapping(typeof(bool)));
    }
}
