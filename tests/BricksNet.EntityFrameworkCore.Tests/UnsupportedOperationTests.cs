using System.Transactions;
using BricksNet.EntityFrameworkCore.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BricksNet.EntityFrameworkCore;

/// <summary>Operations Databricks (or this provider, so far) cannot perform fail clearly.</summary>
public sealed class UnsupportedOperationTests
{
    [Fact]
    public void BeginTransaction_Throws()
    {
        using var context = BloggingContext.Create(new FakeDatabricks());

        var ex = Assert.Throws<NotSupportedException>(() => context.Database.BeginTransaction());

        Assert.Contains("multi-statement transactions", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BeginTransactionAsync_Throws()
    {
        await using var context = BloggingContext.Create(new FakeDatabricks());

        await Assert.ThrowsAsync<NotSupportedException>(() => context.Database.BeginTransactionAsync());
    }

    [Fact]
    public void UseTransaction_Null_IsAllowed()
    {
        using var context = BloggingContext.Create(new FakeDatabricks());

        Assert.Null(context.Database.UseTransaction(null));
    }

    [Fact]
    public async Task AmbientTransaction_Throws_InsteadOfBeingIgnored()
    {
        var databricks = new FakeDatabricks();
        await using var context = BloggingContext.Create(databricks);
        using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.Blogs.ToListAsync());
        Assert.Empty(databricks.Statements);
    }

    [Fact]
    public void Migrate_Throws()
    {
        using var context = BloggingContext.Create(new FakeDatabricks());

        var ex = Assert.Throws<NotSupportedException>(() => context.Database.Migrate());

        Assert.Contains("Migrations are not supported", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureCreated_Throws()
    {
        using var context = BloggingContext.Create(new FakeDatabricks());

        Assert.Throws<NotSupportedException>(() => context.Database.EnsureCreated());
    }
}
