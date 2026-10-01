using Apache.Arrow;
using Apache.Arrow.Types;
using Moq;
using Xunit;

namespace BricksNet.Results;

public class ArrowTypeConverterTests
{
    // ---- Null handling ----

    [Fact]
    public void ConvertValue_ReturnsNull_ForNullCell()
    {
        var array = new Int32Array.Builder().AppendNull().Build();
        Assert.Null(ArrowTypeConverter.ConvertValue(array, 0));
    }

    // ---- Integer types ----

    [Fact]
    public void ConvertValue_Int8_ReturnsSByte()
    {
        var array = new Int8Array.Builder().Append(42).Build();
        var result = ArrowTypeConverter.ConvertValue(array, 0);
        Assert.Equal((sbyte)42, Assert.IsType<sbyte>(result));
    }

    [Fact]
    public void ConvertValue_Int16_ReturnsShort()
    {
        var array = new Int16Array.Builder().Append(1234).Build();
        Assert.Equal((short)1234, Assert.IsType<short>(ArrowTypeConverter.ConvertValue(array, 0)));
    }

    [Fact]
    public void ConvertValue_Int32_ReturnsInt()
    {
        var array = new Int32Array.Builder().Append(99_999).Build();
        Assert.Equal(99_999, Assert.IsType<int>(ArrowTypeConverter.ConvertValue(array, 0)));
    }

    [Fact]
    public void ConvertValue_Int64_ReturnsLong()
    {
        var array = new Int64Array.Builder().Append(long.MaxValue).Build();
        Assert.Equal(long.MaxValue, Assert.IsType<long>(ArrowTypeConverter.ConvertValue(array, 0)));
    }

    [Fact]
    public void ConvertValue_UInt8_ReturnsByte()
    {
        var array = new UInt8Array.Builder().Append(255).Build();
        Assert.Equal((byte)255, Assert.IsType<byte>(ArrowTypeConverter.ConvertValue(array, 0)));
    }

    [Fact]
    public void ConvertValue_UInt16_ReturnsUShort()
    {
        var array = new UInt16Array.Builder().Append(65535).Build();
        Assert.Equal((ushort)65535, Assert.IsType<ushort>(ArrowTypeConverter.ConvertValue(array, 0)));
    }

    [Fact]
    public void ConvertValue_UInt32_ReturnsUInt()
    {
        var array = new UInt32Array.Builder().Append(uint.MaxValue).Build();
        Assert.Equal(uint.MaxValue, Assert.IsType<uint>(ArrowTypeConverter.ConvertValue(array, 0)));
    }

    [Fact]
    public void ConvertValue_UInt64_ReturnsULong()
    {
        var array = new UInt64Array.Builder().Append(ulong.MaxValue).Build();
        Assert.Equal(ulong.MaxValue, Assert.IsType<ulong>(ArrowTypeConverter.ConvertValue(array, 0)));
    }

    // ---- Floating-point types ----

    [Fact]
    public void ConvertValue_Float_ReturnsFloat()
    {
        var array = new FloatArray.Builder().Append(3.14f).Build();
        Assert.Equal(3.14f, Assert.IsType<float>(ArrowTypeConverter.ConvertValue(array, 0)));
    }

    [Fact]
    public void ConvertValue_Double_ReturnsDouble()
    {
        var array = new DoubleArray.Builder().Append(2.718281828).Build();
        Assert.Equal(2.718281828, Assert.IsType<double>(ArrowTypeConverter.ConvertValue(array, 0)));
    }

    // ---- Boolean ----

    [Fact]
    public void ConvertValue_Boolean_ReturnsBool()
    {
        var array = new BooleanArray.Builder().Append(true).Build();
        Assert.True(Assert.IsType<bool>(ArrowTypeConverter.ConvertValue(array, 0)));
    }

    // ---- Decimal ----

    [Fact]
    public void ConvertValue_Decimal128_ReturnsDecimal()
    {
        var type = new Decimal128Type(precision: 10, scale: 2);
        var array = new Decimal128Array.Builder(type).Append(123.45m).Build();
        Assert.Equal(123.45m, Assert.IsType<decimal>(ArrowTypeConverter.ConvertValue(array, 0)));
    }

    [Fact]
    public void ConvertValue_Decimal128_BeyondSystemDecimal_ReturnsExactString()
    {
        // DECIMAL(38,0) can hold 38 digits; System.Decimal holds at most ~29.
        const string digits = "12345678901234567890123456789012345678";
        var type = new Decimal128Type(precision: 38, scale: 0);
        var array = new Decimal128Array.Builder(type)
            .Append(System.Data.SqlTypes.SqlDecimal.Parse(digits))
            .Build();

        Assert.Equal(digits, Assert.IsType<string>(ArrowTypeConverter.ConvertValue(array, 0)));
    }

    // ---- Date types ----

    [Fact]
    public void ConvertValue_Date32_ReturnsDateOnly()
    {
        var expected = new DateOnly(2024, 3, 15);
        var array = new Date32Array.Builder().Append(new DateTime(2024, 3, 15)).Build();

        var result = ArrowTypeConverter.ConvertValue(array, 0);
        Assert.Equal(expected, Assert.IsType<DateOnly>(result));
    }

    [Fact]
    public void ConvertValue_Date64_ReturnsDateOnly()
    {
        var expected = new DateOnly(2024, 6, 1);
        var array = new Date64Array.Builder().Append(new DateTime(2024, 6, 1)).Build();

        var result = ArrowTypeConverter.ConvertValue(array, 0);
        Assert.Equal(expected, Assert.IsType<DateOnly>(result));
    }

    // ---- Timestamp ----

    [Fact]
    public void ConvertValue_Timestamp_ReturnsDateTimeOffset()
    {
        var tsType = new TimestampType(TimeUnit.Microsecond, "UTC");
        var expected = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero);
        var array = new TimestampArray.Builder(tsType).Append(expected).Build();

        var result = ArrowTypeConverter.ConvertValue(array, 0);
        Assert.IsType<DateTimeOffset>(result);
        // Compare at second precision to avoid unit-rounding differences.
        Assert.Equal(expected.ToUnixTimeSeconds(), ((DateTimeOffset)result!).ToUnixTimeSeconds());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ConvertValue_TimestampWithoutTimezone_ReturnsWallClockDateTime(string? timezone)
    {
        // Spark writes TIMESTAMP_NTZ as an Arrow timestamp with no time zone.
        var tsType = new TimestampType(TimeUnit.Microsecond, timezone!);
        var array = new TimestampArray.Builder(tsType)
            .Append(new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero))
            .Build();

        var result = Assert.IsType<DateTime>(ArrowTypeConverter.ConvertValue(array, 0));

        Assert.Equal(new DateTime(2024, 1, 15, 10, 30, 0), result);
        Assert.Equal(DateTimeKind.Unspecified, result.Kind);
    }

    // ---- String types ----

    [Fact]
    public void ConvertValue_Utf8_ReturnsString()
    {
        var array = new StringArray.Builder().Append("hello").Build();
        Assert.Equal("hello", Assert.IsType<string>(ArrowTypeConverter.ConvertValue(array, 0)));
    }

    [Fact]
    public void ConvertValue_Utf8_NullReturnsNull()
    {
        var array = new StringArray.Builder().AppendNull().Build();
        Assert.Null(ArrowTypeConverter.ConvertValue(array, 0));
    }

    // ---- Binary types ----

    [Fact]
    public void ConvertValue_Binary_ReturnsByteArray()
    {
        var expected = new byte[] { 1, 2, 3 };
        var array = new BinaryArray.Builder().Append((ReadOnlySpan<byte>)expected).Build();
        Assert.Equal(expected, Assert.IsType<byte[]>(ArrowTypeConverter.ConvertValue(array, 0)));
    }

    // ---- List type ----

    [Fact]
    public void ConvertValue_List_ReturnsObjectArray()
    {
        // Build a List<int32> array with one list cell: [10, 20, 30]
        var valueBuilder = new Int32Array.Builder();
        valueBuilder.Append(10).Append(20).Append(30);
        var valueArray = valueBuilder.Build();

        var listOffsets = new ArrowBuffer.Builder<int>().Append(0).Append(3).Build();
        var listType = new ListType(new Field("item", Int32Type.Default, nullable: false));
        var listData = new ArrayData(
            listType,
            length: 1,
            nullCount: 0,
            offset: 0,
            buffers: new[] { ArrowBuffer.Empty, listOffsets },
            children: new[] { valueArray.Data });
        var listArray = new ListArray(listData);

        var result = ArrowTypeConverter.ConvertValue(listArray, 0);
        var cells = Assert.IsType<object?[]>(result);
        Assert.Equal(3, cells.Length);
        Assert.Equal(10, cells[0]);
        Assert.Equal(20, cells[1]);
        Assert.Equal(30, cells[2]);
    }

    // ---- Unsupported type ----

    [Fact]
    public void ConvertValue_UnsupportedType_ThrowsNotSupportedException()
    {
        // A Moq proxy won't match any concrete Arrow array type in the switch,
        // so the _ arm throws NotSupportedException.
        var mockData = new ArrayData(
            Int32Type.Default, length: 1, nullCount: 0, offset: 0,
            buffers: new[] { ArrowBuffer.Empty, new ArrowBuffer.Builder<int>().Append(0).Build() },
            children: System.Array.Empty<ArrayData>());
        var mockArray = new Mock<IArrowArray>(MockBehavior.Strict);
        mockArray.Setup(a => a.IsNull(It.IsAny<int>())).Returns(false);
        mockArray.Setup(a => a.Data).Returns(mockData);

        Assert.Throws<NotSupportedException>(() => ArrowTypeConverter.ConvertValue(mockArray.Object, 0));
    }

    // ---- ConvertBatch column-name mapping ----

    [Fact]
    public void ConvertBatch_MapsColumnNames()
    {
        var schema = new Schema(
            new List<Field>
            {
                new Field("id", Int32Type.Default, nullable: false),
                new Field("name", StringType.Default, nullable: true),
            },
            metadata: null);

        var idArray = new Int32Array.Builder().Append(7).Build();
        var nameArray = new StringArray.Builder().Append("Alice").Build();

        using var batch = new RecordBatch(schema, new IArrowArray[] { idArray, nameArray }, length: 1);
        var rows = ArrowTypeConverter.ConvertBatch(batch);

        Assert.Single(rows);
        Assert.Equal(7, rows[0]["id"]);
        Assert.Equal("Alice", rows[0]["name"]);
    }

    [Fact]
    public void ConvertBatch_MultipleRows()
    {
        var schema = new Schema(
            new List<Field> { new Field("v", Int32Type.Default, nullable: true) },
            metadata: null);

        var values = new Int32Array.Builder().Append(1).Append(2).Append(3).Build();
        using var batch = new RecordBatch(schema, new IArrowArray[] { values }, length: 3);

        var rows = ArrowTypeConverter.ConvertBatch(batch);
        Assert.Equal(3, rows.Count);
        Assert.Equal(1, rows[0]["v"]);
        Assert.Equal(2, rows[1]["v"]);
        Assert.Equal(3, rows[2]["v"]);
    }
}
