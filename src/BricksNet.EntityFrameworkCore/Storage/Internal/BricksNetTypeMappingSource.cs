using System.Data;
using BricksNet.EntityFrameworkCore.Storage.Internal.Mapping;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BricksNet.EntityFrameworkCore.Storage.Internal;

/// <summary>
/// Maps CLR types to Databricks SQL types and back, including facets (<c>DECIMAL(p,s)</c>) and
/// store-type aliases (<c>INTEGER</c>, <c>LONG</c>, <c>VARCHAR(n)</c>, ...).
/// </summary>
/// <remarks>
/// <para>
/// Databricks has no unsigned integers, so <see cref="byte"/>, <see cref="ushort"/>, <see cref="uint"/>
/// and <see cref="ulong"/> map to the next wider signed type. <see cref="Guid"/>, <see cref="char"/>,
/// <see cref="TimeOnly"/> and <see cref="TimeSpan"/> are stored as <c>STRING</c>.
/// </para>
/// <para>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </para>
/// </remarks>
public class BricksNetTypeMappingSource : RelationalTypeMappingSource
{
    private static readonly BricksNetBoolTypeMapping Bool = new();
    private static readonly BricksNetSByteTypeMapping TinyInt = new();
    private static readonly BricksNetByteTypeMapping ByteAsSmallInt = new();
    private static readonly BricksNetShortTypeMapping SmallInt = new();
    private static readonly UShortTypeMapping UShortAsInt = new("INT", DbType.UInt16);
    private static readonly IntTypeMapping Int = new("INT", DbType.Int32);
    private static readonly BricksNetUIntTypeMapping UIntAsBigInt = new();
    private static readonly BricksNetLongTypeMapping BigInt = new();
    private static readonly BricksNetULongTypeMapping ULongAsDecimal = new();
    private static readonly BricksNetFloatTypeMapping Float = new();
    private static readonly BricksNetDoubleTypeMapping Double = new();
    private static readonly BricksNetDecimalTypeMapping Decimal = new();
    private static readonly BricksNetStringTypeMapping String = new();
    private static readonly BricksNetDateOnlyTypeMapping Date = new();
    private static readonly BricksNetDateTimeTypeMapping TimestampNtz = new();
    private static readonly BricksNetDateTimeOffsetTypeMapping Timestamp = new();
    private static readonly BricksNetByteArrayTypeMapping Binary = new();
    private static readonly GuidTypeMapping GuidAsString = new("STRING", DbType.Guid);
    private static readonly RelationalTypeMapping CharAsString = (RelationalTypeMapping)String.WithComposedConverter(new CharToStringConverter());
    private static readonly RelationalTypeMapping TimeOnlyAsString = (RelationalTypeMapping)String.WithComposedConverter(new TimeOnlyToStringConverter());
    private static readonly RelationalTypeMapping TimeSpanAsString = (RelationalTypeMapping)String.WithComposedConverter(new TimeSpanToStringConverter());

    private static readonly Dictionary<Type, RelationalTypeMapping> ClrTypeMappings = new()
    {
        [typeof(bool)] = Bool,
        [typeof(sbyte)] = TinyInt,
        [typeof(byte)] = ByteAsSmallInt,
        [typeof(short)] = SmallInt,
        [typeof(ushort)] = UShortAsInt,
        [typeof(int)] = Int,
        [typeof(uint)] = UIntAsBigInt,
        [typeof(long)] = BigInt,
        [typeof(ulong)] = ULongAsDecimal,
        [typeof(float)] = Float,
        [typeof(double)] = Double,
        [typeof(decimal)] = Decimal,
        [typeof(string)] = String,
        [typeof(char)] = CharAsString,
        [typeof(DateOnly)] = Date,
        [typeof(DateTime)] = TimestampNtz,
        [typeof(DateTimeOffset)] = Timestamp,
        [typeof(TimeOnly)] = TimeOnlyAsString,
        [typeof(TimeSpan)] = TimeSpanAsString,
        [typeof(byte[])] = Binary,
        [typeof(Guid)] = GuidAsString,
    };

    // Keyed by the store type name without facets; facets such as DECIMAL(10,2) or VARCHAR(50)
    // are applied from the mapping info.
    private static readonly Dictionary<string, RelationalTypeMapping> StoreTypeMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BOOLEAN"] = Bool,
        ["TINYINT"] = TinyInt,
        ["BYTE"] = TinyInt,
        ["SMALLINT"] = SmallInt,
        ["SHORT"] = SmallInt,
        ["INT"] = Int,
        ["INTEGER"] = Int,
        ["BIGINT"] = BigInt,
        ["LONG"] = BigInt,
        ["FLOAT"] = Float,
        ["REAL"] = Float,
        ["DOUBLE"] = Double,
        ["DECIMAL"] = Decimal,
        ["DEC"] = Decimal,
        ["NUMERIC"] = Decimal,
        ["STRING"] = String,
        ["VARCHAR"] = String,
        ["CHAR"] = String,
        ["DATE"] = Date,
        ["TIMESTAMP_NTZ"] = TimestampNtz,
        ["TIMESTAMP"] = Timestamp,
        ["TIMESTAMP_LTZ"] = Timestamp,
        ["BINARY"] = Binary,
    };

    /// <summary>Initialises a new instance.</summary>
    public BricksNetTypeMappingSource(
        TypeMappingSourceDependencies dependencies,
        RelationalTypeMappingSourceDependencies relationalDependencies)
        : base(dependencies, relationalDependencies)
    {
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping? FindMapping(in RelationalTypeMappingInfo mappingInfo) =>
        base.FindMapping(mappingInfo) ?? FindRawMapping(mappingInfo)?.WithTypeMappingInfo(mappingInfo);

    private static RelationalTypeMapping? FindRawMapping(RelationalTypeMappingInfo mappingInfo)
    {
        // Databricks rejects DECIMAL precision above 38; report no mapping rather than emit invalid DDL/casts.
        if (mappingInfo.Precision > BricksNetDecimalTypeMapping.MaxPrecision)
        {
            return null;
        }

        var clrType = mappingInfo.ClrType;
        var storeTypeNameBase = mappingInfo.StoreTypeNameBase;

        if (storeTypeNameBase is not null
            && StoreTypeMappings.TryGetValue(storeTypeNameBase, out var storeMapping)
            && (clrType is null || storeMapping.ClrType == clrType))
        {
            return storeMapping;
        }

        return clrType is not null && ClrTypeMappings.TryGetValue(clrType, out var clrMapping)
            ? clrMapping
            : null;
    }
}
