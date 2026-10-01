using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace BricksNet.EntityFrameworkCore.Query.Internal.Translators;

/// <summary>
/// Translates members of <see cref="DateTime"/> (<c>TIMESTAMP_NTZ</c>), <see cref="DateTimeOffset"/>
/// (<c>TIMESTAMP</c>) and <see cref="DateOnly"/> (<c>DATE</c>), and the current-time properties.
/// </summary>
/// <remarks>
/// <see cref="DateTimeOffset"/> parts are evaluated in the Databricks session time zone, which is
/// UTC unless changed; values are read back in UTC.
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class BricksNetDateTimeMemberTranslator : IMemberTranslator
{
    private static readonly Dictionary<string, string> DatePartFunctions = new(StringComparer.Ordinal)
    {
        [nameof(DateTime.Year)] = "year",
        [nameof(DateTime.Month)] = "month",
        [nameof(DateTime.Day)] = "day",
        [nameof(DateTime.Hour)] = "hour",
        [nameof(DateTime.Minute)] = "minute",
        [nameof(DateTime.Second)] = "second",
        [nameof(DateTime.DayOfYear)] = "dayofyear",
    };

    private readonly ISqlExpressionFactory _sql;
    private readonly IRelationalTypeMappingSource _typeMappingSource;
    private readonly RelationalTypeMapping _intMapping;

    /// <summary>Initialises a new instance.</summary>
    public BricksNetDateTimeMemberTranslator(ISqlExpressionFactory sqlExpressionFactory, IRelationalTypeMappingSource typeMappingSource)
    {
        _sql = sqlExpressionFactory;
        _typeMappingSource = typeMappingSource;
        _intMapping = typeMappingSource.FindMapping(typeof(int))!;
    }

    /// <inheritdoc />
    public virtual SqlExpression? Translate(
        SqlExpression? instance,
        MemberInfo member,
        Type returnType,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        var declaringType = member.DeclaringType;
        if (declaringType != typeof(DateTime) && declaringType != typeof(DateTimeOffset) && declaringType != typeof(DateOnly))
        {
            return null;
        }

        if (instance is null)
        {
            return TranslateStatic(declaringType, member.Name);
        }

        if (DatePartFunctions.TryGetValue(member.Name, out var function))
        {
            return _sql.Function(function, [instance], typeof(int), _intMapping);
        }

        switch (member.Name)
        {
            case nameof(DateTime.DayOfWeek):
                // Databricks numbers Sunday as 1; .NET DayOfWeek numbers it 0.
                return _sql.Convert(
                    _sql.Subtract(_sql.Function("dayofweek", [instance], typeof(int), _intMapping), _sql.Constant(1, _intMapping)),
                    typeof(DayOfWeek),
                    _intMapping);

            case nameof(DateTime.Date) when declaringType == typeof(DateTime):
                return _sql.Convert(_sql.Convert(instance, typeof(DateOnly), Mapping(typeof(DateOnly))), typeof(DateTime), Mapping(typeof(DateTime)));

            case nameof(DateTimeOffset.Date) when declaringType == typeof(DateTimeOffset):
                return _sql.Convert(_sql.Convert(instance, typeof(DateOnly), Mapping(typeof(DateOnly))), typeof(DateTime), Mapping(typeof(DateTime)));

            case nameof(DateTimeOffset.UtcDateTime) or nameof(DateTimeOffset.DateTime) when declaringType == typeof(DateTimeOffset):
                return _sql.Convert(instance, typeof(DateTime), Mapping(typeof(DateTime)));

            default:
                return null;
        }
    }

    private SqlExpression? TranslateStatic(Type declaringType, string memberName)
    {
        var dateTimeMapping = Mapping(typeof(DateTime));

        if (declaringType == typeof(DateTime))
        {
            return memberName switch
            {
                nameof(DateTime.Now) => NoArgumentFunction("localtimestamp", typeof(DateTime), dateTimeMapping),
                nameof(DateTime.UtcNow) => _sql.Function(
                    "convert_timezone",
                    [_sql.Constant("UTC", Mapping(typeof(string))), NoArgumentFunction("localtimestamp", typeof(DateTime), dateTimeMapping)],
                    typeof(DateTime),
                    dateTimeMapping),
                nameof(DateTime.Today) => _sql.Convert(
                    NoArgumentFunction("current_date", typeof(DateOnly), Mapping(typeof(DateOnly))),
                    typeof(DateTime),
                    dateTimeMapping),
                _ => null,
            };
        }

        if (declaringType == typeof(DateTimeOffset) && memberName is nameof(DateTimeOffset.Now) or nameof(DateTimeOffset.UtcNow))
        {
            return NoArgumentFunction("current_timestamp", typeof(DateTimeOffset), Mapping(typeof(DateTimeOffset)));
        }

        return null;
    }

    private SqlExpression NoArgumentFunction(string name, Type returnType, RelationalTypeMapping typeMapping) =>
        _sql.Function(name, [], nullable: false, [], returnType, typeMapping);

    private RelationalTypeMapping Mapping(Type clrType) => _typeMappingSource.FindMapping(clrType)!;
}
