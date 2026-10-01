using BricksNet.AdoNet;
using BricksNet.EntityFrameworkCore.FunctionalTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BricksNet.EntityFrameworkCore.FunctionalTests;

/// <summary>
/// The query shapes on the live-verification checklist, each compared with LINQ-to-Objects over the
/// same seeded data.
/// </summary>
[Collection(LiveCollection.Name)]
[Trait("Category", "E2E")]
public sealed class LiveQueryTests(LiveDatabaseFixture fixture)
{
    [LiveFact]
    public async Task ServerVersion_IsReported()
    {
        await using var context = fixture.CreateContext();
        await context.Database.OpenConnectionAsync();

        var version = await ((BricksNetConnection)context.Database.GetDbConnection()).GetServerVersionAsync();

        Assert.True(Version.TryParse(version, out _), $"Unexpected server version '{version}'.");
    }

    [LiveFact]
    public async Task Filtering_Ordering_And_Paging()
    {
        await using var context = fixture.CreateContext();
        var city = "Oslo";

        var server = await context.Customers.Where(c => c.IsActive && c.City != city)
            .OrderBy(c => c.Name).Skip(2).Take(5).Select(c => c.Id).ToListAsync();
        var client = fixture.Customers.Where(c => c.IsActive && c.City != city)
            .OrderBy(c => c.Name, StringComparer.Ordinal).Skip(2).Take(5).Select(c => c.Id).ToList();

        Assert.Equal(client, server);
    }

    [LiveFact]
    public async Task Skip_WithoutTake_UsesStandaloneOffset()
    {
        await using var context = fixture.CreateContext();

        var server = await context.Orders.OrderBy(o => o.Id).Skip(40).Select(o => o.Id).ToListAsync();

        Assert.Equal(fixture.Orders.OrderBy(o => o.Id).Skip(40).Select(o => o.Id), server);
    }

    [LiveFact]
    public async Task Any_And_All_AsScalars_UseExistsInProjection()
    {
        await using var context = fixture.CreateContext();

        Assert.Equal(fixture.Orders.Any(o => o.Amount > 200), await context.Orders.AnyAsync(o => o.Amount > 200));
        Assert.Equal(fixture.Orders.All(o => o.Quantity > 0), await context.Orders.AllAsync(o => o.Quantity > 0));
    }

    [LiveFact]
    public async Task Aggregates_And_GroupBy()
    {
        await using var context = fixture.CreateContext();

        Assert.Equal(fixture.Orders.Count, await context.Orders.CountAsync());
        Assert.Equal(fixture.Orders.Sum(o => o.Quantity), await context.Orders.SumAsync(o => o.Quantity));
        Assert.Equal(fixture.Orders.Average(o => o.Quantity), await context.Orders.AverageAsync(o => o.Quantity), 10);

        var server = await context.Orders.GroupBy(o => o.Status)
            .Select(g => new { g.Key, Count = g.Count(), Total = g.Sum(o => o.Amount) }).ToListAsync();
        var client = fixture.Orders.GroupBy(o => o.Status)
            .Select(g => new { g.Key, Count = g.Count(), Total = g.Sum(o => o.Amount) }).ToList();
        Assert.Equal(client.OrderBy(x => x.Key), server.OrderBy(x => x.Key));
    }

    [LiveFact]
    public async Task FirstOrDefault_InProjection_IsACorrelatedSubqueryWithLimit()
    {
        await using var context = fixture.CreateContext();

        var server = await context.Customers.OrderBy(c => c.Id)
            .Select(c => new { c.Id, Latest = c.Orders.OrderByDescending(o => o.OrderDate).Select(o => o.Amount).FirstOrDefault() })
            .ToListAsync();
        var client = fixture.Customers.OrderBy(c => c.Id)
            .Select(c => new { c.Id, Latest = c.Orders.OrderByDescending(o => o.OrderDate).Select(o => o.Amount).FirstOrDefault() })
            .ToList();

        Assert.Equal(client, server);
    }

    [LiveFact]
    public async Task CrossAndOuterApply_UseLateralJoins()
    {
        await using var context = fixture.CreateContext();

        var cross = await context.Customers
            .SelectMany(c => context.Orders.Where(o => o.Amount > c.Id * 10).OrderBy(o => o.Id).Take(2), (c, o) => new { c.Id, OrderId = o.Id })
            .ToListAsync();
        var expectedCross = fixture.Customers
            .SelectMany(c => fixture.Orders.Where(o => o.Amount > c.Id * 10).OrderBy(o => o.Id).Take(2), (c, o) => new { c.Id, OrderId = o.Id })
            .ToList();
        Assert.Equal(expectedCross.OrderBy(x => x.Id).ThenBy(x => x.OrderId), cross.OrderBy(x => x.Id).ThenBy(x => x.OrderId));

        var outer = await context.Customers
            .SelectMany(
                c => context.Orders.Where(o => o.Amount > c.Id * 20).OrderBy(o => o.Id).Take(1).DefaultIfEmpty(),
                (c, o) => new { c.Id, OrderId = (int?)o!.Id })
            .ToListAsync();
        Assert.Equal(fixture.Customers.Count, outer.Count);
    }

    [LiveFact]
    public async Task GroupBy_FirstPerGroup_UsesRowNumber()
    {
        await using var context = fixture.CreateContext();

        var server = await context.Orders.GroupBy(o => o.CustomerId).Select(g => g.OrderBy(o => o.Amount).ThenBy(o => o.Id).First().Id).ToListAsync();
        var client = fixture.Orders.GroupBy(o => o.CustomerId).Select(g => g.OrderBy(o => o.Amount).ThenBy(o => o.Id).First().Id).ToList();

        Assert.Equal(client.Order(), server.Order());
    }

    [LiveFact]
    public async Task Include_SingleAndSplitQuery()
    {
        await using var context = fixture.CreateContext();

        var single = await context.Customers.Include(c => c.Orders).AsNoTracking().ToListAsync();
        var split = await context.Customers.Include(c => c.Orders).AsSplitQuery().AsNoTracking().ToListAsync();

        foreach (var loaded in new[] { single, split })
        {
            Assert.Equal(fixture.Customers.Count, loaded.Count);
            Assert.Equal(
                fixture.Customers.OrderBy(c => c.Id).Select(c => c.Orders.Count),
                loaded.OrderBy(c => c.Id).Select(c => c.Orders.Count));
            Assert.Equal(
                fixture.Customers.OrderBy(c => c.Id).Select(c => c.Address.PostCode),
                loaded.OrderBy(c => c.Id).Select(c => c.Address.PostCode));
        }
    }

    [LiveFact]
    public async Task StringJoin_OverGroup()
    {
        await using var context = fixture.CreateContext();

        var server = await context.Orders.GroupBy(o => o.CustomerId)
            .Select(g => new { g.Key, Statuses = string.Join(",", g.Select(o => o.Status)) }).ToListAsync();

        foreach (var group in server)
        {
            // collect_list has no guaranteed order, so compare as sorted multisets.
            var expected = fixture.Orders.Where(o => o.CustomerId == group.Key).Select(o => o.Status).Order();
            Assert.Equal(expected, group.Statuses.Split(',').Order());
        }
    }

    [LiveFact]
    public async Task SetOperations_And_Contains()
    {
        await using var context = fixture.CreateContext();
        var ids = new List<int> { 1, 3, 5, 999 };

        var union = await context.Customers.Select(c => c.City).Union(context.Customers.Select(c => c.Address.PostCode)).ToListAsync();
        var contains = await context.Customers.Where(c => ids.Contains(c.Id)).Select(c => c.Id).ToListAsync();

        Assert.Equal(
            fixture.Customers.Select(c => c.City).Union(fixture.Customers.Select(c => c.Address.PostCode)).Order(),
            union.Order());
        Assert.Equal([1, 3, 5], contains.Order());
    }

    [LiveFact]
    public async Task IntegerDivision_And_StringConcatenation()
    {
        await using var context = fixture.CreateContext();

        var server = await context.Orders.OrderBy(o => o.Id).Select(o => new { Half = o.Quantity / 2, Label = o.Status + "-" + o.Quantity }).ToListAsync();
        var client = fixture.Orders.OrderBy(o => o.Id).Select(o => new { Half = o.Quantity / 2, Label = o.Status + "-" + o.Quantity }).ToList();

        Assert.Equal(client, server);
    }
}
