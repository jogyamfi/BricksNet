using BricksNet.Contracts.Options;
using Xunit;

namespace BricksNet.Backends;

public sealed class ParameterFormatterTests
{
    private static (string? Value, string? Type) Format(object? value, string? typeHint = null) =>
        ParameterFormatter.Format(new BricksNetParameter("p", value, typeHint));

    public static TheoryData<object?, string?, string?> Cases() => new()
    {
        { null, null, null },
        { "text", "text", "STRING" },
        { true, "true", "BOOLEAN" },
        { (sbyte)-1, "-1", "TINYINT" },
        { (short)7, "7", "SMALLINT" },
        { 42, "42", "INT" },
        { 9_007_199_254_740_993L, "9007199254740993", "BIGINT" },
        { 1.5f, "1.5", "FLOAT" },
        { 0.1d, "0.1", "DOUBLE" },
        { 12345678901234567890.123456789m, "12345678901234567890.123456789", "DECIMAL(29,9)" },
        { 0.05m, "0.05", "DECIMAL(2,2)" },
        { new DateOnly(2026, 9, 25), "2026-09-25", "DATE" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Format_RendersInvariantValue_AndInfersType(object? value, string? expectedValue, string? expectedType)
    {
        var (formatted, type) = Format(value);

        Assert.Equal(expectedValue, formatted);
        Assert.Equal(expectedType, type);
    }

    [Fact]
    public void Format_DateTimeOffset_IsIso8601_WithOffset()
    {
        var (value, type) = Format(new DateTimeOffset(2026, 9, 25, 13, 45, 30, TimeSpan.FromHours(2)));

        Assert.Equal("2026-09-25T13:45:30+02:00", value);
        Assert.Equal("TIMESTAMP", type);
    }

    [Fact]
    public void Format_UnspecifiedDateTime_IsTimestampNtz()
    {
        var (value, type) = Format(new DateTime(2026, 9, 25, 8, 0, 0, DateTimeKind.Unspecified));

        Assert.Equal("2026-09-25T08:00:00", value);
        Assert.Equal("TIMESTAMP_NTZ", type);
    }

    [Fact]
    public void Format_TypeHint_OverridesInference()
    {
        Assert.Equal("BIGINT", Format(5, "BIGINT").Type);
    }

    [Fact]
    public void Format_DbNull_IsSqlNull()
    {
        Assert.Equal((null, null), Format(DBNull.Value));
    }

    [Fact]
    public void Format_UnsignedValues_InferATypeWideEnoughForTheirRange()
    {
        // TINYINT and SMALLINT are signed, so byte and ushort need the next wider type.
        Assert.Equal(("200", "SMALLINT"), Format((byte)200));
        Assert.Equal(("60000", "INT"), Format((ushort)60000));
    }

    [Fact]
    public void Format_NullWithTypeHint_KeepsTheType()
    {
        Assert.Equal((null, "INT"), Format(null, "INT"));
    }

    [Fact]
    public void Format_DateTime_AsDate_HasNoTimePart()
    {
        Assert.Equal(("2026-09-25", "DATE"), Format(new DateTime(2026, 9, 25, 8, 30, 0, DateTimeKind.Utc), "DATE"));
        // The hint is matched case-insensitively and sent as given.
        Assert.Equal(("2026-09-25", "date"), Format(new DateTimeOffset(2026, 9, 25, 8, 30, 0, TimeSpan.Zero), "date"));
    }

    [Fact]
    public void Format_UtcDateTime_AsTimestampNtz_HasNoOffset()
    {
        var (value, type) = Format(new DateTime(2026, 9, 25, 8, 30, 0, DateTimeKind.Utc), "TIMESTAMP_NTZ");

        Assert.Equal("2026-09-25T08:30:00", value);
        Assert.Equal("TIMESTAMP_NTZ", type);
    }
}
