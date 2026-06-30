using System.Diagnostics;
using Xunit;

namespace BricksNet.Telemetry;

public sealed class BricksNetActivitySourceTests : IDisposable
{
    private readonly ActivityListener _listener;

    public BricksNetActivitySourceTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == BricksNetActivitySource.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose() => _listener.Dispose();

    private static object? Tag(Activity activity, string name) => activity.GetTagItem(name);

    [Fact]
    public void StartConnectionOpen_SetsCommonAttributes()
    {
        using var activity = BricksNetActivitySource.StartConnectionOpen("host", 443, "rest");

        Assert.NotNull(activity);
        Assert.Equal("databricks", Tag(activity!, BricksNetActivitySource.AttributeNames.DbSystem));
        Assert.Equal("host", Tag(activity!, BricksNetActivitySource.AttributeNames.ServerAddress));
        Assert.Equal(443, Tag(activity!, BricksNetActivitySource.AttributeNames.ServerPort));
        Assert.Equal("rest", Tag(activity!, BricksNetActivitySource.AttributeNames.DatabricksBackend));
    }

    [Fact]
    public void StartConnectionClose_SetsCommonAttributes()
    {
        using var activity = BricksNetActivitySource.StartConnectionClose("host", 443, "thrift");

        Assert.NotNull(activity);
        Assert.Equal("thrift", Tag(activity!, BricksNetActivitySource.AttributeNames.DatabricksBackend));
    }

    [Fact]
    public void StartStatementExecute_OmitsSql_WhenSensitiveLoggingDisabled()
    {
        using var activity = BricksNetActivitySource.StartStatementExecute(
            "SELECT secret", "cat", "sch", "host", 443, "rest", sensitiveDataLoggingEnabled: false);

        Assert.NotNull(activity);
        Assert.Null(Tag(activity!, BricksNetActivitySource.AttributeNames.DbStatement));
        Assert.Equal("cat.sch", Tag(activity!, BricksNetActivitySource.AttributeNames.DbName));
    }

    [Fact]
    public void StartStatementExecute_IncludesTruncatedSql_WhenEnabled()
    {
        var longSql = new string('x', BricksNetActivitySource.MaxStatementLength + 50);

        using var activity = BricksNetActivitySource.StartStatementExecute(
            longSql, null, null, "host", 443, "rest", sensitiveDataLoggingEnabled: true);

        Assert.NotNull(activity);
        var recorded = (string?)Tag(activity!, BricksNetActivitySource.AttributeNames.DbStatement);
        Assert.Equal(BricksNetActivitySource.MaxStatementLength, recorded!.Length);
        Assert.Null(Tag(activity!, BricksNetActivitySource.AttributeNames.DbName));
    }

    [Theory]
    [InlineData("cat", null, "cat")]
    [InlineData(null, "sch", "sch")]
    [InlineData("cat", "sch", "cat.sch")]
    public void StartStatementExecute_BuildsDatabaseName(string? catalog, string? schema, string expected)
    {
        using var activity = BricksNetActivitySource.StartStatementExecute(
            "q", catalog, schema, "host", 443, "rest", sensitiveDataLoggingEnabled: false);

        Assert.Equal(expected, Tag(activity!, BricksNetActivitySource.AttributeNames.DbName));
    }

    [Fact]
    public void StartResultFetch_SetsStatementId()
    {
        using var activity = BricksNetActivitySource.StartResultFetch("stmt-1", "rest");

        Assert.NotNull(activity);
        Assert.Equal("stmt-1", Tag(activity!, BricksNetActivitySource.AttributeNames.DatabricksStatementId));
    }

    [Fact]
    public void StartAuthTokenFetch_SetsAuthType()
    {
        using var activity = BricksNetActivitySource.StartAuthTokenFetch("pat");

        Assert.NotNull(activity);
        Assert.Equal("pat", Tag(activity!, BricksNetActivitySource.AttributeNames.DatabricksAuthType));
    }

    [Fact]
    public void SetStatementId_IgnoresNullActivity_AndEmptyId()
    {
        BricksNetActivitySource.SetStatementId(null, "id");

        using var activity = BricksNetActivitySource.StartResultFetch(null, "rest");
        BricksNetActivitySource.SetStatementId(activity, string.Empty);
        Assert.Null(Tag(activity!, BricksNetActivitySource.AttributeNames.DatabricksStatementId));

        BricksNetActivitySource.SetStatementId(activity, "later");
        Assert.Equal("later", Tag(activity!, BricksNetActivitySource.AttributeNames.DatabricksStatementId));
    }
}
