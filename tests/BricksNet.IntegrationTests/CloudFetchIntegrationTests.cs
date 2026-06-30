using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BricksNet.IntegrationTests.FakeService;
using Xunit;

namespace BricksNet.IntegrationTests;

/// <summary>
/// Replay integration tests for the CloudFetch (external links) result path. The fake server serves
/// the statement response plus two presigned chunk payloads; the handler must download both and
/// surface rows in ascending chunk order.
/// </summary>
public sealed class CloudFetchIntegrationTests : IntegrationTestBase, IDisposable
{
    public CloudFetchIntegrationTests(FakeServiceFixture fixture)
        : base(fixture)
    {
    }

    public void Dispose() => DisposeClients();

    [Fact]
    public async Task CloudFetch_DownloadsExternalChunks_InChunkIndexOrder()
    {
        var replayer = LoadFixtures("cloudfetch", "execute_select");

        await using var client = CreateClient();
        await using var session = await client.OpenSessionAsync();
        await using var operation = await session.ExecuteStatementAsync("SELECT id FROM big_table");

        var rows = await operation.FetchAllAsync<IReadOnlyDictionary<string, object?>>();

        var ids = rows.Select(r => Convert.ToInt32(r["id"])).ToArray();
        Assert.Equal(new[] { 1, 2, 3, 4 }, ids);

        // Both presigned chunk endpoints must have been hit exactly once each.
        Assert.Equal(1, replayer.HitsFor("GET", "/external/cf-0"));
        Assert.Equal(1, replayer.HitsFor("GET", "/external/cf-1"));
    }
}
