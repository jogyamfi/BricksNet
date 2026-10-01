using System.Data;
using Xunit;

namespace BricksNet.AdoNet;

/// <summary>
/// An explicitly assigned <see cref="DbType"/> becomes the SQL type sent with the value, so
/// ORMs that set <see cref="DbType"/> (such as EF Core) get typed parameters, including typed NULLs.
/// </summary>
public sealed class ParameterTypeHintTests
{
    public static TheoryData<DbType, string> DbTypeHints() => new()
    {
        { DbType.Boolean, "BOOLEAN" },
        { DbType.SByte, "TINYINT" },
        { DbType.Byte, "SMALLINT" },
        { DbType.Int16, "SMALLINT" },
        { DbType.UInt16, "INT" },
        { DbType.Int32, "INT" },
        { DbType.UInt32, "BIGINT" },
        { DbType.Int64, "BIGINT" },
        { DbType.UInt64, "DECIMAL(20,0)" },
        { DbType.Single, "FLOAT" },
        { DbType.Double, "DOUBLE" },
        { DbType.String, "STRING" },
        { DbType.AnsiStringFixedLength, "STRING" },
        { DbType.Guid, "STRING" },
        { DbType.Date, "DATE" },
        { DbType.DateTime, "TIMESTAMP_NTZ" },
        { DbType.DateTime2, "TIMESTAMP_NTZ" },
        { DbType.DateTimeOffset, "TIMESTAMP" },
        { DbType.Binary, "BINARY" },
    };

    [Theory]
    [MemberData(nameof(DbTypeHints))]
    public void ExplicitDbType_MapsToSqlType(DbType dbType, string expected)
    {
        var parameter = new BricksNetParameter("p", DBNull.Value) { DbType = dbType };

        Assert.Equal(expected, parameter.GetDbTypeHint());
    }

    [Fact]
    public void UnsetDbType_HasNoHint_AndIsInferredFromValue()
    {
        var parameter = new BricksNetParameter("p", 5L);

        Assert.Null(parameter.GetDbTypeHint());
        Assert.Equal(DbType.Int64, parameter.DbType);
    }

    [Fact]
    public void ResetDbType_ClearsExplicitType()
    {
        var parameter = new BricksNetParameter("p", 5) { DbType = DbType.String };

        parameter.ResetDbType();

        Assert.Null(parameter.GetDbTypeHint());
        Assert.Equal(DbType.Int32, parameter.DbType);
    }

    [Fact]
    public void Decimal_UsesDeclaredPrecisionAndScale()
    {
        var parameter = new BricksNetParameter("p", 1.5m) { DbType = DbType.Decimal, Precision = 10, Scale = 2 };

        Assert.Equal("DECIMAL(10,2)", parameter.GetDbTypeHint());
    }

    [Fact]
    public void Decimal_WithoutPrecision_InfersFromValue_ButTypesNull()
    {
        Assert.Null(new BricksNetParameter("p", 1.5m) { DbType = DbType.Decimal }.GetDbTypeHint());
        Assert.Equal("DECIMAL(38,18)", new BricksNetParameter("p", null) { DbType = DbType.Decimal }.GetDbTypeHint());
    }

    [Fact]
    public void Command_SendsExplicitTypeHint_OverDbType_AndDbTypeOverInference()
    {
        using var connection = new BricksNetConnection(
            "Host=h;HttpPath=/sql/1.0/warehouses/x;Token=tok",
            (_, _) => new FakeFluent.FakeClient([]));
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO t VALUES (:a, :b, :c)";
        command.Parameters.Add(new BricksNetParameter(":a", null) { DbType = DbType.Int32 });
        command.Parameters.Add(new BricksNetParameter(":b", 1) { DbType = DbType.Int64, TypeHint = "SMALLINT" });
        command.Parameters.Add(new BricksNetParameter(":c", "x"));

        command.ExecuteNonQuery();

        var sent = ((FakeFluent.FakeSession)connection.GetOpenSession()).LastOptions!.Parameters!;
        Assert.Equal(["INT", "SMALLINT", null], sent.Select(p => p.TypeHint));
        Assert.Equal(["a", "b", "c"], sent.Select(p => p.Name));
    }
}
