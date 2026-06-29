using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

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

    /// <inheritdoc />
    public override DbType DbType { get; set; } = DbType.String;

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
    /// When unset the server infers the type from <see cref="Value"/>.
    /// </summary>
    public string? TypeHint { get; set; }

    /// <inheritdoc />
    public override void ResetDbType() => DbType = DbType.String;
}
