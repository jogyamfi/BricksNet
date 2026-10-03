using System.Data;
using BricksNet.EntityFrameworkCore.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace BricksNet.EntityFrameworkCore.Storage;

public sealed class TypeMappingTests
{
    private static readonly AllTypesContext Context = AllTypesContext.Create(new FakeDatabricks());

    private static IRelationalTypeMappingSource Mappings => Context.GetService<IRelationalTypeMappingSource>();

    private static RelationalTypeMapping MappingFor(Type clrType) => Mappings.FindMapping(clrType)!;

    // ---- CLR type -> store type ----

    [Theory]
    [InlineData(typeof(bool), "BOOLEAN", DbType.Boolean)]
    [InlineData(typeof(sbyte), "TINYINT", DbType.SByte)]
    [InlineData(typeof(byte), "SMALLINT", DbType.Byte)]
    [InlineData(typeof(short), "SMALLINT", DbType.Int16)]
    [InlineData(typeof(ushort), "INT", DbType.UInt16)]
    [InlineData(typeof(int), "INT", DbType.Int32)]
    [InlineData(typeof(uint), "BIGINT", DbType.UInt32)]
    [InlineData(typeof(long), "BIGINT", DbType.Int64)]
    [InlineData(typeof(ulong), "DECIMAL(20,0)", DbType.UInt64)]
    [InlineData(typeof(float), "FLOAT", DbType.Single)]
    [InlineData(typeof(double), "DOUBLE", DbType.Double)]
    [InlineData(typeof(decimal), "DECIMAL(18,2)", DbType.Decimal)]
    [InlineData(typeof(string), "STRING", DbType.String)]
    [InlineData(typeof(char), "STRING", DbType.String)]
    [InlineData(typeof(DateOnly), "DATE", DbType.Date)]
    [InlineData(typeof(DateTime), "TIMESTAMP_NTZ", DbType.DateTime2)]
    [InlineData(typeof(DateTimeOffset), "TIMESTAMP", DbType.DateTimeOffset)]
    [InlineData(typeof(TimeOnly), "STRING", DbType.String)]
    [InlineData(typeof(TimeSpan), "STRING", DbType.String)]
    [InlineData(typeof(byte[]), "BINARY", DbType.Binary)]
    [InlineData(typeof(Guid), "STRING", DbType.Guid)]
    public void ClrType_MapsToStoreType_AndDbType(Type clrType, string storeType, DbType dbType)
    {
        var mapping = MappingFor(clrType);

        Assert.Equal(storeType, mapping.StoreType);
        Assert.Equal(dbType, mapping.DbType);
    }

    // ---- store type -> CLR type, including aliases and facets ----

    [Theory]
    [InlineData("boolean", typeof(bool), "boolean")]
    [InlineData("BYTE", typeof(sbyte), "BYTE")]
    [InlineData("SHORT", typeof(short), "SHORT")]
    [InlineData("INTEGER", typeof(int), "INTEGER")]
    [InlineData("bigint", typeof(long), "bigint")]
    [InlineData("LONG", typeof(long), "LONG")]
    [InlineData("REAL", typeof(float), "REAL")]
    [InlineData("DECIMAL(10,2)", typeof(decimal), "DECIMAL(10,2)")]
    [InlineData("decimal(38, 6)", typeof(decimal), "decimal(38,6)")]
    [InlineData("NUMERIC(5,0)", typeof(decimal), "NUMERIC(5,0)")]
    [InlineData("VARCHAR(50)", typeof(string), "VARCHAR(50)")]
    [InlineData("CHAR(3)", typeof(string), "CHAR(3)")]
    [InlineData("TIMESTAMP_LTZ", typeof(DateTimeOffset), "TIMESTAMP_LTZ")]
    [InlineData("timestamp_ntz", typeof(DateTime), "timestamp_ntz")]
    [InlineData("BINARY", typeof(byte[]), "BINARY")]
    public void StoreType_MapsToClrType(string storeType, Type clrType, string expectedStoreType)
    {
        var mapping = Mappings.FindMapping(storeType)!;

        Assert.Equal(clrType, mapping.ClrType);
        Assert.Equal(expectedStoreType, mapping.StoreType);
    }

    [Fact]
    public void StoreType_DecimalFacets_AreParsed()
    {
        var mapping = Mappings.FindMapping("DECIMAL(10,4)")!;

        Assert.Equal(10, mapping.Precision);
        Assert.Equal(4, mapping.Scale);
    }

    [Theory]
    [InlineData("DECIMAL(39,2)")]
    [InlineData("STRUCT<a: INT>")]
    [InlineData("NOT_A_TYPE")]
    public void UnsupportedStoreTypes_HaveNoMapping(string storeType) =>
        Assert.Null(Mappings.FindMapping(storeType));

    [Fact]
    public void ExplicitColumnType_WithDifferentClrType_KeepsTheColumnType()
    {
        var mapping = Mappings.FindMapping(typeof(int), "BIGINT")!;

        Assert.Equal(typeof(int), mapping.ClrType);
        Assert.Equal("BIGINT", mapping.StoreType);
    }

    // ---- facets configured in the model ----

    [Theory]
    [InlineData(nameof(AllTypes.Decimal), "DECIMAL(18,2)")]
    [InlineData(nameof(AllTypes.Money), "DECIMAL(10,4)")]
    [InlineData(nameof(AllTypes.Code), "VARCHAR(12)")]
    [InlineData(nameof(AllTypes.Note), "STRING")]
    [InlineData(nameof(AllTypes.Mood), "INT")]
    [InlineData(nameof(AllTypes.NullableInt), "INT")]
    [InlineData(nameof(AllTypes.ULong), "DECIMAL(20,0)")]
    public void ModelProperties_GetDatabricksColumnTypes(string property, string storeType)
    {
        var entityType = Context.Model.FindEntityType(typeof(AllTypes))!;

        Assert.Equal(storeType, entityType.FindProperty(property)!.GetColumnType());
    }

    // ---- literals ----

    public static TheoryData<object, string> Literals() => new()
    {
        { true, "true" },
        { false, "false" },
        { (sbyte)-128, "-128Y" },
        { (byte)200, "200S" },
        { (short)-7, "-7S" },
        { (ushort)60000, "60000" },
        { 42, "42" },
        { 4_000_000_000u, "4000000000L" },
        { long.MinValue, "-9223372036854775808L" },
        { ulong.MaxValue, "18446744073709551615BD" },
        { 1.5f, "1.5F" },
        { 0.1f, "0.1F" },
        { float.NaN, "CAST('NaN' AS FLOAT)" },
        { 0.1d, "0.1D" },
        { 1e20d, "1E+20D" },
        { double.PositiveInfinity, "CAST('Infinity' AS DOUBLE)" },
        { double.NegativeInfinity, "CAST('-Infinity' AS DOUBLE)" },
        { 1.50m, "1.50BD" },
        { -0.001m, "-0.001BD" },
        { @"it's \ fine", @"'it\'s \\ fine'" },
        { new DateOnly(2026, 3, 4), "DATE'2026-03-04'" },
        { new DateTime(2026, 3, 4, 5, 6, 7), "TIMESTAMP_NTZ'2026-03-04 05:06:07'" },
        { new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc).AddTicks(1_234_567), "TIMESTAMP_NTZ'2026-03-04 05:06:07.123456'" },
        { new DateTime(2026, 3, 4, 5, 6, 7).AddTicks(5_000_000), "TIMESTAMP_NTZ'2026-03-04 05:06:07.5'" },
        { new DateTimeOffset(2026, 3, 4, 10, 0, 0, TimeSpan.FromHours(2)), "TIMESTAMP'2026-03-04 08:00:00+00:00'" },
        { new byte[] { 0x0A, 0xFF }, "X'0AFF'" },
        { Guid.Parse("11112222-3333-4444-5555-666677778888"), "'11112222-3333-4444-5555-666677778888'" },
    };

    [Theory]
    [MemberData(nameof(Literals))]
    public void Literals_UseDatabricksSyntax(object value, string expected) =>
        Assert.Equal(expected, MappingFor(value.GetType()).GenerateSqlLiteral(value));

    [Fact]
    public void Literals_ForConvertedTypes_AreStrings()
    {
        Assert.Equal("'Z'", MappingFor(typeof(char)).GenerateSqlLiteral('Z'));
        Assert.Equal("'13:45:30'", MappingFor(typeof(TimeOnly)).GenerateSqlLiteral(new TimeOnly(13, 45, 30)));
        Assert.Equal("'1.02:03:04'", MappingFor(typeof(TimeSpan)).GenerateSqlLiteral(new TimeSpan(1, 2, 3, 4)));
    }

    [Fact]
    public void Null_Literal_IsNULL() => Assert.Equal("NULL", MappingFor(typeof(int)).GenerateSqlLiteral(null));

    [Fact]
    public void DecimalLiteral_WithPrecisionFacet_StillUsesValuePrecision() =>
        Assert.Equal("1.23456BD", Mappings.FindMapping("DECIMAL(10,2)")!.GenerateSqlLiteral(1.23456m));
}
