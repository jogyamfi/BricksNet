using BricksNet.EntityFrameworkCore.FunctionalTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BricksNet.EntityFrameworkCore.FunctionalTests;

/// <summary>Every mapped type survives a round trip, and typed literals are accepted.</summary>
[Collection(LiveCollection.Name)]
[Trait("Category", "E2E")]
public sealed class LiveTypeTests(LiveDatabaseFixture fixture)
{
    private const long TicksPerMicrosecond = 10;

    private static DateTime ToMicroseconds(DateTime value) => new(value.Ticks - (value.Ticks % TicksPerMicrosecond), value.Kind);

    [LiveFact]
    public async Task EveryType_RoundTrips()
    {
        await using var context = fixture.CreateContext();

        var loaded = await context.TypeRows.AsNoTracking().OrderBy(r => r.Id).ToListAsync();

        Assert.Equal(fixture.TypeRows.Count, loaded.Count);
        foreach (var (expected, actual) in fixture.TypeRows.Zip(loaded))
        {
            Assert.Equal(expected.Bool, actual.Bool);
            Assert.Equal(expected.SByte, actual.SByte);
            Assert.Equal(expected.Byte, actual.Byte);
            Assert.Equal(expected.Short, actual.Short);
            Assert.Equal(expected.UShort, actual.UShort);
            Assert.Equal(expected.UInt, actual.UInt);
            Assert.Equal(expected.Long, actual.Long);
            Assert.Equal(expected.ULong, actual.ULong);
            Assert.Equal(expected.Float, actual.Float);
            Assert.Equal(expected.Double, actual.Double);
            Assert.Equal(expected.Decimal, actual.Decimal);
            Assert.Equal(expected.Text, actual.Text);
            Assert.Equal(expected.Char, actual.Char);
            Assert.Equal(expected.Date, actual.Date);

            // Databricks stores microseconds; TIMESTAMP_NTZ keeps the wall clock, TIMESTAMP the instant (read back in UTC).
            Assert.Equal(ToMicroseconds(expected.DateTime), DateTime.SpecifyKind(actual.DateTime, DateTimeKind.Unspecified));
            Assert.Equal(expected.DateTimeOffset.ToUniversalTime(), actual.DateTimeOffset);
            Assert.Equal(TimeSpan.Zero, actual.DateTimeOffset.Offset);

            Assert.Equal(expected.TimeOnly, actual.TimeOnly);
            Assert.Equal(expected.TimeSpan, actual.TimeSpan);
            Assert.Equal(expected.Bytes, actual.Bytes);
            Assert.Equal(expected.Guid, actual.Guid);
            Assert.Equal(expected.NullableInt, actual.NullableInt);
            Assert.Equal(expected.NullableText, actual.NullableText);
        }
    }

    [LiveFact]
    public async Task TypedLiterals_AreAcceptedAndMatch()
    {
        await using var context = fixture.CreateContext();

        // Constants are inlined as Databricks typed literals: 5Y, 200S, 9000000000L, 1234.5678BD,
        // DATE'…', TIMESTAMP_NTZ'…', TIMESTAMP'…', X'…', 1.5F, 2.25D.
        var ids = await context.TypeRows
            .Where(r => r.SByte == 5 && r.Byte == 200 && r.Long == 9_000_000_000 && r.Decimal == 1234.5678m
                && r.Date == new DateOnly(2026, 3, 4)
                && r.DateTime > new DateTime(2026, 3, 4, 5, 6, 7)
                && r.DateTimeOffset == new DateTimeOffset(2026, 3, 4, 8, 0, 0, TimeSpan.Zero)
                && r.Bytes == new byte[] { 1, 2, 3 }
                && r.Float == 1.5f && r.Double == 2.25 && r.Text == "plain" && r.Char == 'Z')
            .Select(r => r.Id)
            .ToListAsync();

        Assert.Equal([1], ids);
    }

    [LiveFact]
    public async Task StringLiterals_WithQuotesAndBackslashes_Match()
    {
        await using var context = fixture.CreateContext();

        var found = await context.TypeRows.Where(r => r.Text == "quote ' backslash \\ newline \n unicode é漢").Select(r => r.Id).ToListAsync();

        Assert.Equal([2], found);
    }
}
