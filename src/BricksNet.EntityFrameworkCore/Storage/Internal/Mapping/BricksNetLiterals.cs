using System.Globalization;
using System.Text;

namespace BricksNet.EntityFrameworkCore.Storage.Internal.Mapping;

/// <summary>Formats .NET values as Databricks SQL typed literals.</summary>
internal static class BricksNetLiterals
{
    private const long TicksPerMicrosecond = TimeSpan.TicksPerMillisecond / 1000;

    /// <summary>An integral literal with a Databricks type suffix (<c>Y</c>, <c>S</c>, <c>L</c>, <c>BD</c>).</summary>
    public static string Integral(IFormattable value, string suffix) =>
        value.ToString(null, CultureInfo.InvariantCulture) + suffix;

    /// <summary>
    /// A <c>FLOAT</c>/<c>DOUBLE</c> literal (suffix <c>F</c>/<c>D</c>). NaN and infinities have no
    /// literal form and are cast from their string names.
    /// </summary>
    public static string FloatingPoint(double value, string suffix, string storeType) => value switch
    {
        double.NaN => $"CAST('NaN' AS {storeType})",
        double.PositiveInfinity => $"CAST('Infinity' AS {storeType})",
        double.NegativeInfinity => $"CAST('-Infinity' AS {storeType})",
        _ => value.ToString("R", CultureInfo.InvariantCulture) + suffix,
    };

    /// <summary>
    /// A <c>FLOAT</c> literal, formatted at single precision: widening to <see cref="double"/>
    /// first would print <c>0.1f</c> as <c>0.10000000149011612</c>.
    /// </summary>
    public static string FloatingPoint(float value, string suffix, string storeType) => float.IsFinite(value)
        ? value.ToString("R", CultureInfo.InvariantCulture) + suffix
        : FloatingPoint((double)value, suffix, storeType);

    /// <summary>
    /// <c>yyyy-MM-dd HH:mm:ss[.ffffff]</c>, truncated to the microsecond precision Databricks stores,
    /// with trailing zeros of the fraction omitted.
    /// </summary>
    public static string DateTimeText(DateTime value)
    {
        var builder = new StringBuilder(value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        var microseconds = value.Ticks % TimeSpan.TicksPerSecond / TicksPerMicrosecond;
        if (microseconds != 0)
        {
            builder.Append('.').Append(microseconds.ToString("D6", CultureInfo.InvariantCulture).TrimEnd('0'));
        }

        return builder.ToString();
    }

    /// <summary>A <c>DATE</c> literal.</summary>
    public static string Date(DateOnly value) =>
        "DATE'" + value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "'";

    /// <summary>A <c>TIMESTAMP_NTZ</c> (wall-clock) literal; the <see cref="DateTime.Kind"/> is ignored.</summary>
    public static string TimestampNtz(DateTime value) => "TIMESTAMP_NTZ'" + DateTimeText(value) + "'";

    /// <summary>A <c>TIMESTAMP</c> (instant) literal, normalised to UTC.</summary>
    public static string Timestamp(DateTimeOffset value) => "TIMESTAMP'" + DateTimeText(value.UtcDateTime) + "+00:00'";

    /// <summary>A <c>BINARY</c> literal: <c>X'0A0B'</c>.</summary>
    public static string Binary(byte[] value) => "X'" + Convert.ToHexString(value) + "'";
}
