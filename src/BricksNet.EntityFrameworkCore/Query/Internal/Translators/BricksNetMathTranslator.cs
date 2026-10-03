using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace BricksNet.EntityFrameworkCore.Query.Internal.Translators;

/// <summary>
/// Translates <see cref="Math"/> and <see cref="MathF"/> methods to Databricks math functions.
/// </summary>
/// <remarks>
/// <see cref="Math.Max(int, int)"/> and <see cref="Math.Min(int, int)"/> are translated by
/// <see cref="BricksNetSqlTranslatingExpressionVisitor"/>, where EF routes them.
/// <see cref="Math.Round(double)"/> rounds half to even, as Databricks <c>bround</c> does; Databricks
/// <c>round</c> rounds half away from zero and is used for <see cref="MidpointRounding.AwayFromZero"/>.
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class BricksNetMathTranslator : IMethodCallTranslator
{
    private static readonly Dictionary<string, string> SimpleFunctions = new(StringComparer.Ordinal)
    {
        [nameof(Math.Abs)] = "abs",
        [nameof(Math.Sqrt)] = "sqrt",
        [nameof(Math.Cbrt)] = "cbrt",
        [nameof(Math.Exp)] = "exp",
        [nameof(Math.Log10)] = "log10",
        [nameof(Math.Log2)] = "log2",
        [nameof(Math.Pow)] = "pow",
        [nameof(Math.Sin)] = "sin",
        [nameof(Math.Cos)] = "cos",
        [nameof(Math.Tan)] = "tan",
        [nameof(Math.Asin)] = "asin",
        [nameof(Math.Acos)] = "acos",
        [nameof(Math.Atan)] = "atan",
        [nameof(Math.Atan2)] = "atan2",
    };

    private static readonly HashSet<Type> NumericTypes =
    [
        typeof(double), typeof(float), typeof(decimal), typeof(int), typeof(long), typeof(short), typeof(sbyte),
    ];

    private readonly ISqlExpressionFactory _sql;
    private readonly IRelationalTypeMappingSource _typeMappingSource;

    /// <summary>Initialises a new instance.</summary>
    public BricksNetMathTranslator(ISqlExpressionFactory sqlExpressionFactory, IRelationalTypeMappingSource typeMappingSource)
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
        if ((method.DeclaringType != typeof(Math) && method.DeclaringType != typeof(MathF))
            || !NumericTypes.Contains(method.ReturnType)
            || arguments.Count == 0)
        {
            return null;
        }

        var returnType = method.ReturnType;
        var resultMapping = arguments[0].TypeMapping is { } argumentMapping && argumentMapping.ClrType == returnType
            ? argumentMapping
            : _typeMappingSource.FindMapping(returnType);

        switch (method.Name)
        {
            case nameof(Math.Ceiling):
            case nameof(Math.Floor):
                // Databricks returns BIGINT for non-decimal input; cast back to the .NET result type.
                var roundedType = RoundedType(returnType);
                return AsReturnType(
                    _sql.Function(method.Name == nameof(Math.Ceiling) ? "ceil" : "floor", [arguments[0]], roundedType),
                    roundedType,
                    returnType,
                    resultMapping);

            case nameof(Math.Truncate):
                // Toward zero: floor for positive values, ceil for negative ones.
                var value = arguments[0];
                var truncatedType = RoundedType(returnType);
                return AsReturnType(
                    _sql.Case(
                        [new CaseWhenClause(_sql.LessThan(value, _sql.Constant(0, value.TypeMapping)), _sql.Function("ceil", [value], truncatedType))],
                        _sql.Function("floor", [value], truncatedType)),
                    truncatedType,
                    returnType,
                    resultMapping);

            case nameof(Math.Round):
                return TranslateRound(arguments, returnType, resultMapping);

            case nameof(Math.Log) when arguments.Count == 1:
                return AsReturnType(_sql.Function("ln", [arguments[0]], typeof(double)), typeof(double), returnType, resultMapping);

            case nameof(Math.Log) when arguments.Count == 2:
                // Math.Log(value, base) => log(base, value).
                return AsReturnType(_sql.Function("log", [arguments[1], arguments[0]], typeof(double)), typeof(double), returnType, resultMapping);

            case nameof(Math.Sign):
                // Databricks sign() returns DOUBLE; .NET returns int.
                return _sql.Convert(_sql.Function("sign", [arguments[0]], typeof(double)), typeof(int), _typeMappingSource.FindMapping(typeof(int)));

            default:
                if (!SimpleFunctions.TryGetValue(method.Name, out var function))
                {
                    return null;
                }

                // abs keeps its input type; the other functions return DOUBLE.
                return function == "abs"
                    ? _sql.Function(function, arguments, returnType, resultMapping)
                    : AsReturnType(_sql.Function(function, arguments, typeof(double)), typeof(double), returnType, resultMapping);
        }
    }

    private SqlExpression? TranslateRound(IReadOnlyList<SqlExpression> arguments, Type returnType, RelationalTypeMapping? resultMapping)
    {
        SqlExpression? digits = null;
        var mode = MidpointRounding.ToEven;

        foreach (var argument in arguments.Skip(1))
        {
            if (argument.Type == typeof(MidpointRounding))
            {
                if (argument is not SqlConstantExpression { Value: MidpointRounding constantMode })
                {
                    return null;
                }

                mode = constantMode;
            }
            else
            {
                digits = argument;
            }
        }

        var function = mode switch
        {
            MidpointRounding.ToEven => "bround",
            MidpointRounding.AwayFromZero => "round",
            _ => null,
        };
        if (function is null)
        {
            return null;
        }

        // round/bround keep the input type.
        return _sql.Function(function, digits is null ? [arguments[0]] : [arguments[0], digits], returnType, resultMapping);
    }

    // ceil/floor return DECIMAL for decimal input and BIGINT otherwise.
    private static Type RoundedType(Type returnType) => returnType == typeof(decimal) ? typeof(decimal) : typeof(long);

    // Casts only when the Databricks result type differs from the .NET return type.
    private SqlExpression AsReturnType(SqlExpression expression, Type databricksType, Type returnType, RelationalTypeMapping? resultMapping) =>
        databricksType == returnType
            ? _sql.ApplyTypeMapping(expression, resultMapping)
            : _sql.Convert(expression, returnType, resultMapping);
}
