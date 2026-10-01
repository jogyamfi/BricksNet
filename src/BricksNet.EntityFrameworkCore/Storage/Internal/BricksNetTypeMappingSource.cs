using System.Data;
using BricksNet.EntityFrameworkCore.Storage.Internal.Mapping;
using Microsoft.EntityFrameworkCore.Storage;

namespace BricksNet.EntityFrameworkCore.Storage.Internal;

/// <summary>
/// Maps CLR types to Databricks SQL types and back. This is the baseline set of scalar mappings;
/// facets such as <c>DECIMAL(p,s)</c> precision are refined by the full type-mapping layer.
/// </summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class BricksNetTypeMappingSource : RelationalTypeMappingSource
{
    private static readonly BricksNetBoolTypeMapping Bool = new();
    private static readonly SByteTypeMapping TinyInt = new("TINYINT", DbType.SByte);
    // TINYINT is signed, so byte widens to SMALLINT.
    private static readonly ByteTypeMapping ByteAsSmallInt = new("SMALLINT", DbType.Byte);
    private static readonly ShortTypeMapping SmallInt = new("SMALLINT", DbType.Int16);
    private static readonly IntTypeMapping Int = new("INT", DbType.Int32);
    private static readonly LongTypeMapping BigInt = new("BIGINT", DbType.Int64);
    private static readonly FloatTypeMapping Float = new("FLOAT", DbType.Single);
    private static readonly DoubleTypeMapping Double = new("DOUBLE", DbType.Double);
    private static readonly DecimalTypeMapping Decimal = new("DECIMAL(18,2)", DbType.Decimal, precision: 18, scale: 2);
    private static readonly BricksNetStringTypeMapping String = new();
    private static readonly DateOnlyTypeMapping Date = new("DATE", DbType.Date);
    private static readonly DateTimeTypeMapping TimestampNtz = new("TIMESTAMP_NTZ", DbType.DateTime2);
    private static readonly DateTimeOffsetTypeMapping Timestamp = new("TIMESTAMP", DbType.DateTimeOffset);
    private static readonly ByteArrayTypeMapping Binary = new("BINARY", DbType.Binary);
    private static readonly GuidTypeMapping GuidAsString = new("STRING", DbType.Guid);

    private static readonly Dictionary<Type, RelationalTypeMapping> ClrTypeMappings = new()
    {
        [typeof(bool)] = Bool,
        [typeof(sbyte)] = TinyInt,
        [typeof(byte)] = ByteAsSmallInt,
        [typeof(short)] = SmallInt,
        [typeof(int)] = Int,
        [typeof(long)] = BigInt,
        [typeof(float)] = Float,
        [typeof(double)] = Double,
        [typeof(decimal)] = Decimal,
        [typeof(string)] = String,
        [typeof(DateOnly)] = Date,
        [typeof(DateTime)] = TimestampNtz,
        [typeof(DateTimeOffset)] = Timestamp,
        [typeof(byte[])] = Binary,
        [typeof(Guid)] = GuidAsString,
    };

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
