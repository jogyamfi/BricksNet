using System.Globalization;

namespace BricksNet.AdoNet;

/// <summary>
/// Converts a materialised column value to the type requested through
/// <see cref="BricksNetDataReader.GetFieldValue{T}(int)"/>. Handles the conversions
/// <see cref="Convert.ChangeType(object, Type, IFormatProvider)"/> cannot perform — date/time
/// types, <see cref="Guid"/>, enums and nullable targets — and reports failures as an
/// <see cref="InvalidCastException"/> naming the column and both types.
/// </summary>
internal static class ReaderValueConverter
{
    private const DateTimeStyles UtcStyles = DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal;

    /// <summary>Converts a non-null <paramref name="value"/> to <paramref name="targetType"/>.</summary>
    /// <exception cref="InvalidCastException">The value cannot be represented as the target type.</exception>
    public static object ChangeType(object value, Type targetType, string columnName)
    {
        var target = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (target.IsInstanceOfType(value))
        {
            return value;
        }

        try
        {
            return Convert(value, target)
                ?? throw CreateException(value, targetType, columnName, innerException: null);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or InvalidCastException or ArgumentException)
        {
            throw CreateException(value, targetType, columnName, ex);
        }
    }

    private static object? Convert(object value, Type target)
    {
        if (target.IsEnum)
        {
            return value is string name
                ? Enum.Parse(target, name, ignoreCase: true)
                : Enum.ToObject(target, System.Convert.ChangeType(value, Enum.GetUnderlyingType(target), CultureInfo.InvariantCulture));
        }

        if (target == typeof(DateTime))
        {
            return value switch
            {
                DateTimeOffset dto => dto.UtcDateTime,
                DateOnly date => date.ToDateTime(TimeOnly.MinValue),
                string text => DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                _ => null,
            };
        }

        if (target == typeof(DateTimeOffset))
        {
            return value switch
            {
                // A wall-clock (TIMESTAMP_NTZ) value carries no zone; treat it as UTC.
                DateTime { Kind: DateTimeKind.Unspecified } dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
                DateTime dt => new DateTimeOffset(dt),
                DateOnly date => new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)),
                string text => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, UtcStyles),
                _ => null,
            };
        }

        if (target == typeof(DateOnly))
        {
            return value switch
            {
                DateTime dt => DateOnly.FromDateTime(dt),
                DateTimeOffset dto => DateOnly.FromDateTime(dto.UtcDateTime),
                string text => DateOnly.Parse(text, CultureInfo.InvariantCulture),
                _ => null,
            };
        }

        if (target == typeof(TimeOnly))
        {
            return value switch
            {
                TimeSpan span => TimeOnly.FromTimeSpan(span),
                DateTime dt => TimeOnly.FromDateTime(dt),
                string text => TimeOnly.Parse(text, CultureInfo.InvariantCulture),
                _ => null,
            };
        }

        if (target == typeof(TimeSpan))
        {
            return value switch
            {
                TimeOnly time => time.ToTimeSpan(),
                string text => TimeSpan.Parse(text, CultureInfo.InvariantCulture),
                _ => null,
            };
        }

        if (target == typeof(Guid))
        {
            return value switch
            {
                string text => Guid.Parse(text),
                byte[] { Length: 16 } bytes => new Guid(bytes),
                _ => null,
            };
        }

        if (target == typeof(char) && value is string { Length: 1 } single)
        {
            return single[0];
        }

        if (target == typeof(string))
        {
            return value is IFormattable formattable
                ? formattable.ToString(null, CultureInfo.InvariantCulture)
                : value.ToString();
        }

        if (target == typeof(bool) && value is string boolText)
        {
            return bool.Parse(boolText);
        }

        return value is IConvertible
            ? System.Convert.ChangeType(value, target, CultureInfo.InvariantCulture)
            : null;
    }

    private static InvalidCastException CreateException(
        object value,
        Type targetType,
        string columnName,
        Exception? innerException) =>
        new($"Cannot convert the value of column '{columnName}' from '{value.GetType()}' to '{targetType}'.", innerException);
}
