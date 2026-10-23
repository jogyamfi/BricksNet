using BricksNet.EntityFrameworkCore.FunctionalTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BricksNet.EntityFrameworkCore.FunctionalTests;

/// <summary>
/// Writes against a live warehouse: CRUD, optimistic concurrency, multi-row inserts,
/// ExecuteUpdate/ExecuteDelete and retries of Delta write conflicts. Each test uses its own key range.
/// </summary>
[Collection(LiveCollection.Name)]
[Trait("Category", "E2E")]
public sealed class LiveSaveChangesTests(LiveDatabaseFixture fixture)
{
    [LiveFact]
    public async Task Crud_RoundTrip()
    {
        await using (var context = fixture.CreateContext())
        {
            context.Accounts.Add(new Account { Id = 1001, Owner = "ann" });
            Assert.Equal(1, await context.SaveChangesAsync());
        }

        await using (var context = fixture.CreateContext())
        {
            var account = await context.Accounts.SingleAsync(a => a.Id == 1001);
            account.Note = "updated";
            account.Version++;
            Assert.Equal(1, await context.SaveChangesAsync());
        }

        await using (var context = fixture.CreateContext())
        {
            var account = await context.Accounts.SingleAsync(a => a.Id == 1001);
            Assert.Equal("updated", account.Note);
            context.Remove(account);
            Assert.Equal(1, await context.SaveChangesAsync());
        }

        await using (var context = fixture.CreateContext())
        {
            Assert.False(await context.Accounts.AnyAsync(a => a.Id == 1001));
        }
    }

    [LiveFact]
    public async Task ConcurrencyToken_DetectsAConflictingUpdate()
    {
        await using (var setup = fixture.CreateContext())
        {
            setup.Accounts.Add(new Account { Id = 2001, Owner = "bob" });
            await setup.SaveChangesAsync();
        }

        await using var first = fixture.CreateContext();
        await using var second = fixture.CreateContext();
        var fromFirst = await first.Accounts.SingleAsync(a => a.Id == 2001);
        var fromSecond = await second.Accounts.SingleAsync(a => a.Id == 2001);

        fromFirst.Note = "first";
        fromFirst.Version++;
        await first.SaveChangesAsync();

        fromSecond.Note = "second";
        fromSecond.Version++;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [LiveFact]
    public async Task MultiRowInserts_SplitByParameterBudget()
    {
        await using var context = fixture.CreateContext();
        context.BulkItems.AddRange(Enumerable.Range(1, 300).Select(i => new BulkItem { Id = 10_000 + i, GroupId = (i % 2) + 1, Value = i, Status = "new" }));

        Assert.Equal(300, await context.SaveChangesAsync());
        Assert.Equal(300, await context.BulkItems.CountAsync(b => b.Id > 10_000 && b.Id <= 10_300));
    }

    [LiveFact]
    public async Task ExecuteUpdate_And_ExecuteDelete_IncludingJoins()
    {
        await using var context = fixture.CreateContext();
        context.BulkItems.AddRange(Enumerable.Range(1, 20).Select(i => new BulkItem { Id = 20_000 + i, GroupId = (i % 2) + 1, Value = i, Status = "new" }));
        await context.SaveChangesAsync();
        var range = context.BulkItems.Where(b => b.Id > 20_000 && b.Id <= 20_020);

        Assert.Equal(10, await range.Where(b => b.Value % 2 == 0).ExecuteUpdateAsync(s => s.SetProperty(b => b.Status, "even")));
        Assert.Equal(10, await range.Where(b => b.Group!.Region == "north").ExecuteUpdateAsync(s => s.SetProperty(b => b.Value, b => b.Value * 10)));
        Assert.Equal(5, await range.OrderBy(b => b.Id).Take(5).ExecuteUpdateAsync(s => s.SetProperty(b => b.Status, "first")));
        Assert.Equal(3, await range.OrderBy(b => b.Id).Take(3).ExecuteDeleteAsync());

        // Odd items are in the south group; two of them (1 and 3) were deleted above.
        Assert.Equal(8, await range.Where(b => b.Group!.Region == "south").ExecuteDeleteAsync());

        Assert.Equal(9, await range.CountAsync());
    }

    [LiveFact]
    public async Task ConcurrentWriters_SucceedWithRetryOnFailure()
    {
        await using (var setup = fixture.CreateContext())
        {
            setup.Accounts.AddRange(Enumerable.Range(1, 6).Select(i => new Account { Id = 3000 + i, Owner = "w" + i }));
            await setup.SaveChangesAsync();
        }

        // Parallel updates to the same Delta table can conflict (DELTA_CONCURRENT_*); each conflicting
        // statement is retried on its own.
        await Task.WhenAll(Enumerable.Range(1, 6).Select(async i =>
        {
            await using var context = fixture.CreateContext(o => o.EnableRetryOnFailure(8, TimeSpan.FromSeconds(5)));
            var account = await context.Accounts.SingleAsync(a => a.Id == 3000 + i);
            account.Note = "touched";
            account.Version++;
            await context.SaveChangesAsync();
        }));

        await using var verify = fixture.CreateContext();
        Assert.Equal(6, await verify.Accounts.CountAsync(a => a.Id > 3000 && a.Id <= 3006 && a.Note == "touched"));
    }
}
