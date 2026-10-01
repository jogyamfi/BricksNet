using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace BricksNet.AdoNet;

/// <summary>
/// An ADO.NET parameter for a parameterised Databricks statement.
/// Only input parameters are supported — Databricks SQL has no output or return parameters.
/// </summary>
public sealed class BricksNetParameter : DbParameter
{
    private string _parameterName = string.Empty;
    private string _sourceColumn = string.Empty;
    private ParameterDirection _direction = ParameterDirection.Input;
    private DbType? _dbType;

    /// <summary>Initialises an empty parameter.</summary>
    public BricksNetParameter()
    {
    }

    /// <summary>Initialises a named parameter with a value.</summary>
    /// <param name="parameterName">The parameter name (with or without a leading <c>:</c>/<c>@</c>).</param>
    /// <param name="value">The parameter value.</param>
    public BricksNetParameter(string parameterName, object? value)
    {
        _parameterName = parameterName ?? string.Empty;
        Value = value;
    }

    /// <summary>
    /// The parameter's database type. Until assigned it is inferred from <see cref="Value"/>
    /// (<see cref="DbType.String"/> when the value is <see langword="null"/> or not a known type).
    /// An explicitly assigned type determines the server-side SQL type sent with the value
    /// (see <see cref="TypeHint"/>), which also types <see langword="null"/> values.
    /// </summary>
    public override DbType DbType
    {
        get => _dbType ?? InferDbType(Value);
        set => _dbType = value;
    }

    /// <inheritdoc />
    public override byte Precision { get; set; }

    /// <inheritdoc />
    public override byte Scale { get; set; }

    /// <summary>
    /// Parameter direction. Only <see cref="ParameterDirection.Input"/> is supported.
    /// </summary>
    /// <exception cref="NotSupportedException">A non-input direction is assigned.</exception>
    public override ParameterDirection Direction
    {
        get => _direction;
        set
        {
            if (value != ParameterDirection.Input)
            {
                throw new NotSupportedException(
                    "Databricks SQL supports input parameters only. " +
                    $"Direction '{value}' is not supported.");
            }

            _direction = value;
        }
    }

    /// <inheritdoc />
    public override bool IsNullable { get; set; }

    /// <inheritdoc />
    [AllowNull]
    public override string ParameterName
    {
        get => _parameterName;
        set => _parameterName = value ?? string.Empty;
    }

    /// <inheritdoc />
    [AllowNull]
    public override string SourceColumn
    {
        get => _sourceColumn;
        set => _sourceColumn = value ?? string.Empty;
    }

    /// <inheritdoc />
    public override bool SourceColumnNullMapping { get; set; }

    /// <inheritdoc />
    public override object? Value { get; set; }

    /// <inheritdoc />
    public override int Size { get; set; }

    /// <summary>
    /// Optional server-side type hint (e.g. <c>INT</c>, <c>STRING</c>, <c>TIMESTAMP</c>).
    /// Takes precedence over an explicitly assigned <see cref="DbType"/>. When neither is set the
    /// type is inferred from <see cref="Value"/>.
    /// </summary>
    public string? TypeHint { get; set; }

    /// <inheritdoc />
    public override void ResetDbType() => _dbType = null;

    /// <summary>
    /// Maps an explicitly assigned <see cref="DbType"/> to a Databricks SQL type name, or returns
    /// <see langword="null"/> to let the type be inferred from <see cref="Value"/>.
    /// </summary>
    internal string? GetDbTypeHint()
    {
        if (_dbType is not { } dbType)
        {
            return null;
        }

        var isNull = Value is null or DBNull;
        return dbType switch
        {
            DbType.Boolean => "BOOLEAN",
            DbType.SByte => "TINYINT",
            DbType.Byte or DbType.Int16 => "SMALLINT",
            DbType.UInt16 or DbType.Int32 => "INT",
            DbType.UInt32 or DbType.Int64 => "BIGINT",
            DbType.UInt64 => "DECIMAL(20,0)",
            DbType.Single => "FLOAT",
            DbType.Double => "DOUBLE",
            // Without declared precision the exact type is inferred from the value; a NULL still
            // needs a type, so it gets the widest decimal.
            DbType.Decimal or DbType.Currency or DbType.VarNumeric => Precision > 0
                ? string.Create(CultureInfo.InvariantCulture, $"DECIMAL({Precision},{Math.Min(Scale, Precision)})")
                : isNull ? "DECIMAL(38,18)" : null,
            DbType.String or DbType.StringFixedLength or DbType.AnsiString or DbType.AnsiStringFixedLength
                or DbType.Xml or DbType.Guid or DbType.Time => "STRING",
            DbType.Date => "DATE",
            DbType.DateTime or DbType.DateTime2 => "TIMESTAMP_NTZ",
            DbType.DateTimeOffset => "TIMESTAMP",
            DbType.Binary => "BINARY",
            _ => null,
        };
    }

    private static DbType InferDbType(object? value) => value switch
    {
        bool => DbType.Boolean,
        sbyte => DbType.SByte,
        byte => DbType.Byte,
        short => DbType.Int16,
        ushort => DbType.UInt16,
        int => DbType.Int32,
        uint => DbType.UInt32,
        long => DbType.Int64,
        ulong => DbType.UInt64,
        float => DbType.Single,
        double => DbType.Double,
        decimal => DbType.Decimal,
        DateOnly => DbType.Date,
        DateTime => DbType.DateTime2,
        DateTimeOffset => DbType.DateTimeOffset,
        Guid => DbType.Guid,
        byte[] => DbType.Binary,
        _ => DbType.String,
    };
}
