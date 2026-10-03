using BricksNet.EntityFrameworkCore.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BricksNet.EntityFrameworkCore.Storage;

/// <summary>
/// Values travel through the full stack: EF type mapping, BricksNet parameters and the
/// BricksNet data reader, with the fake service standing in for Databricks.
/// </summary>
public sealed class TypeRoundTripTests
{
    private static readonly Guid SampleGuid = Guid.Parse("11112222-3333-4444-5555-666677778888");

    // Columns as Databricks reports them, in the CLR representations BricksNet's readers produce.
    private static readonly (string Name, string Type, object? Value)[] Row =
    [
        ("Id", "INT", 1),
        ("Bool", "BOOLEAN", true),
        ("Byte", "SMALLINT", (short)200),
        ("Bytes", "BINARY", new byte[] { 1, 2, 3 }),
        ("Char", "STRING", "Z"),
        ("Code", "VARCHAR(12)", "ABC"),
        ("Date", "DATE", new DateOnly(2026, 3, 4)),
        ("DateTime", "TIMESTAMP_NTZ", new DateTime(2026, 3, 4, 5, 6, 7)),
        ("DateTimeOffset", "TIMESTAMP", new DateTimeOffset(2026, 3, 4, 8, 0, 0, TimeSpan.Zero)),
        ("Decimal", "DECIMAL(18,2)", 12.34m),
        ("Double", "DOUBLE", 2.5d),
        ("Float", "FLOAT", 1.5f),
        ("Guid", "STRING", SampleGuid.ToString()),
        ("Long", "BIGINT", 9_000_000_000L),
        ("Money", "DECIMAL(10,4)", 1.2345m),
        ("Mood", "INT", 2),
        ("Note", "STRING", null),
        ("NullableInt", "INT", null),
        ("SByte", "TINYINT", (sbyte)-5),
        ("Short", "SMALLINT", (short)-7),
        ("Text", "STRING", "hello"),
        ("TimeOnly", "STRING", "13:45:30"),
        ("TimeSpan", "STRING", "1.02:03:04"),
        ("UInt", "BIGINT", 4_000_000_000L),
        ("ULong", "DECIMAL(20,0)", 18_446_744_073_709_551_615m),
        ("UShort", "INT", 60000),
    ];

    [Fact]
    public async Task Query_MaterialisesEveryMappedType()
    {
        var databricks = new FakeDatabricks
        {
            Responder = _ => FakeResult.Table(
                Row.Select(c => (c.Name, c.Type)).ToArray(),
                Row.Select(c => c.Value).ToArray()),
        };
        await using var context = AllTypesContext.Create(databricks);

        var entity = Assert.Single(await context.AllTypes.AsNoTracking().ToListAsync());

        Assert.True(entity.Bool);
        Assert.Equal((byte)200, entity.Byte);
        Assert.Equal([1, 2, 3], entity.Bytes);
        Assert.Equal('Z', entity.Char);
        Assert.Equal("ABC", entity.Code);
        Assert.Equal(new DateOnly(2026, 3, 4), entity.Date);
        Assert.Equal(new DateTime(2026, 3, 4, 5, 6, 7), entity.DateTime);
        Assert.Equal(new DateTimeOffset(2026, 3, 4, 8, 0, 0, TimeSpan.Zero), entity.DateTimeOffset);
        Assert.Equal(12.34m, entity.Decimal);
        Assert.Equal(2.5d, entity.Double);
        Assert.Equal(1.5f, entity.Float);
        Assert.Equal(SampleGuid, entity.Guid);
        Assert.Equal(9_000_000_000L, entity.Long);
        Assert.Equal(1.2345m, entity.Money);
        Assert.Equal(Mood.Busy, entity.Mood);
        Assert.Null(entity.Note);
        Assert.Null(entity.NullableInt);
        Assert.Equal((sbyte)-5, entity.SByte);
        Assert.Equal((short)-7, entity.Short);
        Assert.Equal("hello", entity.Text);
        Assert.Equal(new TimeOnly(13, 45, 30), entity.TimeOnly);
        Assert.Equal(new TimeSpan(1, 2, 3, 4), entity.TimeSpan);
        Assert.Equal(4_000_000_000u, entity.UInt);
        Assert.Equal(ulong.MaxValue, entity.ULong);
        Assert.Equal((ushort)60000, entity.UShort);
    }

    [Fact]
    public async Task Parameters_CarryDatabricksTypes()
    {
        var databricks = new FakeDatabricks { Responder = _ => FakeResult.AffectedRows(1) };
        await using var context = AllTypesContext.Create(databricks);
        sbyte tiny = -5;
        byte small = 200;
        uint big = 4_000_000_000;
        ulong huge = ulong.MaxValue;
        var date = new DateOnly(2026, 3, 4);
        var wallClock = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);
        var instant = new DateTimeOffset(2026, 3, 4, 10, 0, 0, TimeSpan.FromHours(2));
        var time = new TimeOnly(13, 45, 30);
        var guid = SampleGuid;
        var amount = 1.555m;

        await context.Database.ExecuteSqlAsync(
            $"INSERT INTO t VALUES ({tiny}, {small}, {big}, {huge}, {date}, {wallClock}, {instant}, {time}, {guid}, {amount})");

        var parameters = Assert.Single(databricks.Statements).Parameters;
        Assert.Equal(
            ["TINYINT", "SMALLINT", "BIGINT", "DECIMAL(20,0)", "DATE", "TIMESTAMP_NTZ", "TIMESTAMP", "STRING", "STRING", null],
            parameters.Select(p => p.TypeHint));

        // Converted types are sent as their provider values.
        Assert.Equal("13:45:30", parameters[7].Value);
        // A decimal keeps its own precision rather than being rounded to a column's scale.
        Assert.Equal(1.555m, parameters[9].Value);
    }

    [Fact]
    public async Task QueryParameters_InferTypesFromTheColumn()
    {
        var databricks = new FakeDatabricks { Responder = _ => FakeResult.Table([("Id", "INT")]) };
        await using var context = AllTypesContext.Create(databricks);
        var since = new DateTime(2026, 1, 1);
        var minimum = 1.555m;

        await context.AllTypes.Where(e => e.DateTime >= since && e.Money > minimum).Select(e => e.Id).ToListAsync();

        var statement = Assert.Single(databricks.Statements);
        Assert.Equal(["TIMESTAMP_NTZ", null], statement.Parameters.Select(p => p.TypeHint));
        Assert.Equal(1.555m, statement.Parameters[1].Value);
    }

    [Fact]
    public async Task QueryConstants_AreInlinedAsTypedLiterals()
    {
        var databricks = new FakeDatabricks { Responder = _ => FakeResult.Table([("Id", "INT")]) };
        await using var context = AllTypesContext.Create(databricks);

        await context.AllTypes
            .Where(e => e.Date == new DateOnly(2026, 3, 4) && e.Long > 5L && e.SByte < 3 && e.Note != "x'y")
            .Select(e => e.Id)
            .ToListAsync();

        var sql = Assert.Single(databricks.Statements).Sql;
        Assert.Contains("`a`.`Date` = DATE'2026-03-04'", sql, StringComparison.Ordinal);
        Assert.Contains("`a`.`Long` > 5L", sql, StringComparison.Ordinal);
        Assert.Contains("`a`.`SByte` < 3Y", sql, StringComparison.Ordinal);
        Assert.Contains(@"'x\'y'", sql, StringComparison.Ordinal);
    }
}
