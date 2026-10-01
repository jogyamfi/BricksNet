using System.Diagnostics;
using BricksNet.EntityFrameworkCore.FunctionalTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;

namespace BricksNet.EntityFrameworkCore.FunctionalTests;

/// <summary>
/// Rough timings against a live warehouse, written to the test output. They assert nothing; run them
/// with <c>dotnet test --filter Category=Benchmark --logger "console;verbosity=detailed"</c>.
/// </summary>
[Collection(LiveCollection.Name)]
[Trait("Category", "E2E")]
[Trait("Category", "Benchmark")]
public sealed class LiveBenchmarks(LiveDatabaseFixture fixture, ITestOutputHelper output)
{
    [LiveFact]
    public async Task SaveChanges_InsertThroughput()
    {
        var nextId = 100_000;
        foreach (var (rows, maxBatchSize) in new[] { (1, 1000), (100, 1000), (1000, 1000), (20, 1), (100, 1) })
        {
            await using var context = fixture.CreateContext(o => o.MaxBatchSize(maxBatchSize));
            context.BulkItems.AddRange(Enumerable.Range(0, rows).Select(i => new BulkItem { Id = nextId++, GroupId = 1, Value = i, Status = "bench" }));

            var stopwatch = Stopwatch.StartNew();
            await context.SaveChangesAsync();
            stopwatch.Stop();

            output.WriteLine(
                $"{rows,5} rows, MaxBatchSize {maxBatchSize,4}: {stopwatch.ElapsedMilliseconds,6} ms "
                + $"({stopwatch.Elapsed.TotalMilliseconds / rows:F1} ms/row)");
        }
    }

    [LiveFact]
    public async Task Query_SessionReuse()
    {
        foreach (var keepSessionAlive in new[] { true, false })
        {
            await using var context = fixture.CreateContext(o => o.KeepSessionAlive(keepSessionAlive));
            await context.Customers.CountAsync();

            var stopwatch = Stopwatch.StartNew();
            for (var i = 0; i < 10; i++)
            {
                await context.Customers.CountAsync(c => c.Id > i);
            }

            stopwatch.Stop();
            output.WriteLine($"10 queries, KeepSessionAlive={keepSessionAlive}: {stopwatch.ElapsedMilliseconds} ms");
        }
    }
}
