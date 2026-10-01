using System.Globalization;
using BricksNet.Contracts.Options;

namespace BricksNet.Backends;

/// <summary>
/// Converts a <see cref="BricksNetParameter"/> into the string value and Databricks SQL type name
/// sent to the server. Both the SQL Statement Execution API and Thrift <c>TSparkParameter</c> accept
/// a string value plus a type, and the server parses the string according to that type, so values
/// are rendered in invariant, ISO formats and the type is inferred from the CLR type when the caller
/// supplies no <see cref="BricksNetParameter.TypeHint"/>.
/// </summary>
internal static class ParameterFormatter
{
    private const int MaxDecimalPrecision = 38;

    public static (string? Value, string? Type) Format(BricksNetParameter parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);

        var value = parameter.Value is DBNull ? null : parameter.Value;
        var type = parameter.TypeHint ?? InferType(value);
        return (FormatValue(value, type), type);
    }

    private static string? FormatValue(object? value, string? type) => value switch
    {
        null => null,
        // Render date/time values in the shape of the declared type: DATE takes no time part and
        // TIMESTAMP_NTZ is a wall-clock value, so neither carries an offset.
        DateTime dt when IsType(type, "DATE") => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTimeOffset dto when IsType(type, "DATE") => dto.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTime dt when IsType(type, "TIMESTAMP_NTZ") =>
            dt.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture),
        string s => s,
        bool b => b ? "true" : "false",
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTime dt => dt.Kind == DateTimeKind.Unspecified
            ? dt.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture)
            : new DateTimeOffset(dt).ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz", CultureInfo.InvariantCulture),
        DateTimeOffset dto => dto.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz", CultureInfo.InvariantCulture),
        float f => f.ToString("R", CultureInfo.InvariantCulture),
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        decimal m => m.ToString(CultureInfo.InvariantCulture),
        Guid g => g.ToString("D"),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture),
    };

    private static string? InferType(object? value) => value switch
    {
        null => null,
        bool => "BOOLEAN",
        sbyte => "TINYINT",
        // TINYINT is signed, so byte values above 127 need SMALLINT.
        byte or short => "SMALLINT",
        ushort or int => "INT",
        uint or long => "BIGINT",
        ulong => "DECIMAL(20,0)",
        float => "FLOAT",
        double => "DOUBLE",
        decimal m => DecimalType(m),
        DateOnly => "DATE",
        DateTime { Kind: DateTimeKind.Unspecified } => "TIMESTAMP_NTZ",
        DateTime or DateTimeOffset => "TIMESTAMP",
        _ => "STRING",
    };

    private static bool IsType(string? type, string name) => string.Equals(type, name, StringComparison.OrdinalIgnoreCase);

    // Precision must cover every digit of the value; the scale is the value's own scale.
    private static string DecimalType(decimal value)
    {
        var scale = value.Scale;
        var digits = decimal.Truncate(Math.Abs(value)).ToString(CultureInfo.InvariantCulture).TrimStart('0').Length;
        var precision = Math.Clamp(Math.Max(digits + scale, 1), 1, MaxDecimalPrecision);
        return string.Create(CultureInfo.InvariantCulture, $"DECIMAL({precision},{Math.Min(scale, precision)})");
    }
}
