using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace BricksNet.EntityFrameworkCore.Query.Internal.Translators;

/// <summary>
/// Translates the <c>Add*</c> methods of <see cref="DateTime"/>, <see cref="DateTimeOffset"/> and
/// <see cref="DateOnly"/>, and <see cref="DateOnly.FromDateTime(DateTime)"/>.
/// </summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class BricksNetDateTimeMethodTranslator : IMethodCallTranslator
{
    private const long MicrosecondsPerMillisecond = 1_000;

    private static readonly Dictionary<string, (string Unit, long Microseconds)> Units = new(StringComparer.Ordinal)
    {
        [nameof(DateTime.AddYears)] = ("YEAR", 0),
        [nameof(DateTime.AddMonths)] = ("MONTH", 0),
        [nameof(DateTime.AddDays)] = ("DAY", 86_400_000_000),
        [nameof(DateTime.AddHours)] = ("HOUR", 3_600_000_000),
        [nameof(DateTime.AddMinutes)] = ("MINUTE", 60_000_000),
        [nameof(DateTime.AddSeconds)] = ("SECOND", 1_000_000),
        [nameof(DateTime.AddMilliseconds)] = ("MILLISECOND", MicrosecondsPerMillisecond),
    };

    private static readonly MethodInfo FromDateTime = typeof(DateOnly).GetRuntimeMethod(nameof(DateOnly.FromDateTime), [typeof(DateTime)])!;

    private readonly ISqlExpressionFactory _sql;
    private readonly IRelationalTypeMappingSource _typeMappingSource;

    /// <summary>Initialises a new instance.</summary>
    public BricksNetDateTimeMethodTranslator(ISqlExpressionFactory sqlExpressionFactory, IRelationalTypeMappingSource typeMappingSource)
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
        if (method == FromDateTime)
        {
            return _sql.Convert(arguments[0], typeof(DateOnly), _typeMappingSource.FindMapping(typeof(DateOnly)));
        }

        var declaringType = method.DeclaringType;
        if (instance is null
            || (declaringType != typeof(DateTime) && declaringType != typeof(DateTimeOffset) && declaringType != typeof(DateOnly))
            || arguments.Count != 1
            || !Units.TryGetValue(method.Name, out var unit))
        {
            return null;
        }

        var amount = arguments[0];

        if (declaringType == typeof(DateOnly))
        {
            return method.Name switch
            {
                nameof(DateOnly.AddDays) => _sql.Function("date_add", [instance, amount], typeof(DateOnly), instance.TypeMapping),
                nameof(DateOnly.AddMonths) => _sql.Function("add_months", [instance, amount], typeof(DateOnly), instance.TypeMapping),
                nameof(DateOnly.AddYears) => _sql.Function(
                    "add_months",
                    [instance, _sql.Multiply(amount, _sql.Constant(12, amount.TypeMapping))],
                    typeof(DateOnly),
                    instance.TypeMapping),
                _ => null,
            };
        }

        // timestampadd takes an integer quantity. Whole numbers (the common case, e.g. AddDays(7))
        // use the named unit; fractional amounts are converted to microseconds.
        var (unitName, quantity) = amount.Type == typeof(double)
            ? FractionalQuantity(unit, amount)
            : (unit.Unit, amount);

        return _sql.Function(
            "timestampadd",
            [_sql.Unit(unitName), quantity, instance],
            instance.Type,
            instance.TypeMapping);
    }

    private (string Unit, SqlExpression Quantity) FractionalQuantity((string Unit, long Microseconds) unit, SqlExpression amount)
    {
        var longMapping = _typeMappingSource.FindMapping(typeof(long));

        if (amount is SqlConstantExpression { Value: double value } && value == Math.Truncate(value)
            && value is >= long.MinValue and <= long.MaxValue)
        {
            return (unit.Unit, _sql.Constant((long)value, longMapping));
        }

        // .NET rounds the added interval to the nearest millisecond.
        var microseconds = _sql.Multiply(
            _sql.Function("round", [_sql.Multiply(amount, _sql.Constant((double)unit.Microseconds / MicrosecondsPerMillisecond, amount.TypeMapping))], typeof(double), amount.TypeMapping),
            _sql.Constant((double)MicrosecondsPerMillisecond, amount.TypeMapping));
        return ("MICROSECOND", _sql.Convert(microseconds, typeof(long), longMapping));
    }
}
