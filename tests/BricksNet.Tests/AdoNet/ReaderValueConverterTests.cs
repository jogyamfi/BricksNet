using System.Data;
using Xunit;

namespace BricksNet.AdoNet;

/// <summary>
/// <see cref="BricksNetDataReader.GetFieldValue{T}(int)"/> conversions that
/// <see cref="Convert.ChangeType(object, Type)"/> cannot perform, as used by ORMs such as EF Core.
/// </summary>
public sealed class ReaderValueConverterTests
{
    private enum Color
    {
        Red = 1,
        Green = 2,
    }

    private static readonly Guid SampleGuid = Guid.Parse("11112222-3333-4444-5555-666677778888");

    private static BricksNetDataReader ReaderOver(object value)
    {
        var rows = FakeFluent.Rows(FakeFluent.Row(("v", value)));
        var operation = new FakeFluent.FakeOperation(rows);
        var first = operation.FetchChunkAsync<IReadOnlyDictionary<string, object?>>(10).GetAwaiter().GetResult();
        var reader = new BricksNetDataReader(operation, [], first, 10, CommandBehavior.Default, connectionToClose: null);
        Assert.True(reader.Read());
        return reader;
    }

    private static T Read<T>(object value)
    {
        using var reader = ReaderOver(value);
        return reader.GetFieldValue<T>(0);
    }

    [Fact]
    public void DateOnly_ToDateTime() =>
        Assert.Equal(new DateTime(2026, 3, 4), Read<DateTime>(new DateOnly(2026, 3, 4)));

    [Fact]
    public void DateTimeOffset_ToDateTime_IsUtc()
    {
        var value = Read<DateTime>(new DateTimeOffset(2026, 3, 4, 10, 0, 0, TimeSpan.FromHours(2)));

        Assert.Equal(new DateTime(2026, 3, 4, 8, 0, 0, DateTimeKind.Utc), value);
        Assert.Equal(DateTimeKind.Utc, value.Kind);
    }

    [Fact]
    public void WallClockDateTime_ToDateTimeOffset_IsTreatedAsUtc() =>
        Assert.Equal(
            new DateTimeOffset(2026, 3, 4, 10, 0, 0, TimeSpan.Zero),
            Read<DateTimeOffset>(new DateTime(2026, 3, 4, 10, 0, 0, DateTimeKind.Unspecified)));

    [Fact]
    public void DateOnly_ToDateTimeOffset_IsUtcMidnight() =>
        Assert.Equal(new DateTimeOffset(2026, 3, 4, 0, 0, 0, TimeSpan.Zero), Read<DateTimeOffset>(new DateOnly(2026, 3, 4)));

    [Fact]
    public void Timestamps_ToDateOnly()
    {
        Assert.Equal(new DateOnly(2026, 3, 4), Read<DateOnly>(new DateTime(2026, 3, 4, 23, 0, 0)));
        Assert.Equal(new DateOnly(2026, 3, 4), Read<DateOnly>(new DateTimeOffset(2026, 3, 5, 1, 0, 0, TimeSpan.FromHours(2))));
    }

    [Fact]
    public void Strings_ToTemporalTypes()
    {
        Assert.Equal(new DateOnly(2026, 3, 4), Read<DateOnly>("2026-03-04"));
        Assert.Equal(new TimeOnly(13, 45, 30), Read<TimeOnly>("13:45:30"));
        Assert.Equal(new TimeSpan(1, 2, 3), Read<TimeSpan>("01:02:03"));
        Assert.Equal(new DateTime(2026, 3, 4, 13, 45, 30), Read<DateTime>("2026-03-04T13:45:30"));
        Assert.Equal(
            new DateTimeOffset(2026, 3, 4, 13, 45, 30, TimeSpan.Zero),
            Read<DateTimeOffset>("2026-03-04T15:45:30+02:00"));
    }

    [Fact]
    public void BigintCount_ToInt32() => Assert.Equal(42, Read<int>(42L));

    [Fact]
    public void Narrowing_Overflow_ThrowsInvalidCast_NamingColumnAndTypes()
    {
        var ex = Assert.Throws<InvalidCastException>(() => Read<int>(5_000_000_000L));

        Assert.Contains("'v'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("System.Int64", ex.Message, StringComparison.Ordinal);
        Assert.Contains("System.Int32", ex.Message, StringComparison.Ordinal);
        Assert.IsType<OverflowException>(ex.InnerException);
    }

    [Fact]
    public void SignedTinyint_ToByte() => Assert.Equal((byte)5, Read<byte>((sbyte)5));

    [Fact]
    public void Guid_FromStringAndBytes()
    {
        Assert.Equal(SampleGuid, Read<Guid>(SampleGuid.ToString()));
        Assert.Equal(SampleGuid, Read<Guid>(SampleGuid.ToByteArray()));
    }

    [Fact]
    public void Enum_FromIntegralAndName()
    {
        Assert.Equal(Color.Green, Read<Color>(2L));
        Assert.Equal(Color.Red, Read<Color>("red"));
    }

    [Fact]
    public void Nullable_Targets_ConvertUnderlyingValue()
    {
        Assert.Equal(42, Read<int?>(42L));
        Assert.Equal(new DateOnly(2026, 3, 4), Read<DateOnly?>(new DateTime(2026, 3, 4)));
    }

    [Fact]
    public void Char_FromSingleCharacterString() => Assert.Equal('Z', Read<char>("Z"));

    [Fact]
    public void String_FromFormattable_IsInvariant() => Assert.Equal("1.5", Read<string>(1.5m));

    [Fact]
    public void Bool_FromString() => Assert.True(Read<bool>("true"));

    [Fact]
    public void Incompatible_ThrowsInvalidCast() =>
        Assert.Throws<InvalidCastException>(() => Read<Guid>(42));
}
