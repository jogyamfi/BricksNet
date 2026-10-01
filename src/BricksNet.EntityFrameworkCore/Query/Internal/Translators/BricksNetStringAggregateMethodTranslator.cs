using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace BricksNet.EntityFrameworkCore.Query.Internal.Translators;

/// <summary>
/// Translates <see cref="string.Join(string, IEnumerable{string})"/> and
/// <see cref="string.Concat(IEnumerable{string})"/> over a group to
/// <c>array_join(collect_list(...), separator)</c>.
/// </summary>
/// <remarks>
/// <c>NULL</c> elements are joined as empty strings, as .NET does. Databricks does not guarantee
/// element order, so an ordered source (<c>g.OrderBy(...).Select(...)</c>) is not translated.
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class BricksNetStringAggregateMethodTranslator : IAggregateMethodCallTranslator
{
    private static readonly MethodInfo Join =
        typeof(string).GetRuntimeMethod(nameof(string.Join), [typeof(string), typeof(IEnumerable<string>)])!;

    private static readonly MethodInfo Concat =
        typeof(string).GetRuntimeMethod(nameof(string.Concat), [typeof(IEnumerable<string>)])!;

    private readonly ISqlExpressionFactory _sql;
    private readonly RelationalTypeMapping _stringMapping;

    /// <summary>Initialises a new instance.</summary>
    public BricksNetStringAggregateMethodTranslator(ISqlExpressionFactory sqlExpressionFactory, IRelationalTypeMappingSource typeMappingSource)
    {
        _sql = sqlExpressionFactory;
        _stringMapping = typeMappingSource.FindMapping(typeof(string))!;
    }

    /// <inheritdoc />
    public virtual SqlExpression? Translate(
        MethodInfo method,
        EnumerableExpression source,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if ((method != Join && method != Concat)
            || source.Selector is not SqlExpression element
            || source.Orderings.Count > 0)
        {
            return null;
        }

        // .NET joins a null element as an empty string; collect_list would drop it.
        element = _sql.Coalesce(element, _sql.Constant(string.Empty, _stringMapping), _stringMapping);
        if (source.Predicate is not null)
        {
            element = _sql.Case([new CaseWhenClause(source.Predicate, element)], elseResult: null);
        }

        var collected = _sql.Function(
            source.IsDistinct ? "collect_set" : "collect_list",
            [element],
            nullable: false,
            [false],
            typeof(string[]));
        var separator = method == Join
            ? _sql.ApplyTypeMapping(arguments[0], _stringMapping)
            : _sql.Constant(string.Empty, _stringMapping);

        return _sql.Function("array_join", [collected, separator], nullable: false, [false, false], typeof(string), _stringMapping);
    }
}
