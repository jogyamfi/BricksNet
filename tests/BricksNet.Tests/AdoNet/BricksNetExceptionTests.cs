using Xunit;

namespace BricksNet.AdoNet;

public sealed class BricksNetExceptionTests
{
    [Theory]
    [InlineData("[DELTA_CONCURRENT_APPEND] Files were added by a concurrent update.", "DELTA_CONCURRENT_APPEND")]
    [InlineData("[UNRESOLVED_COLUMN.WITH_SUGGESTION] A column cannot be resolved. SQLSTATE: 42703", "UNRESOLVED_COLUMN.WITH_SUGGESTION")]
    [InlineData("org.apache.hive.service.cli.HiveSQLException: Error running query: [TABLE_OR_VIEW_NOT_FOUND] x", "TABLE_OR_VIEW_NOT_FOUND")]
    [InlineData("Division by zero", null)]
    [InlineData("value [0] is out of range", null)]
    [InlineData("[lowercase_marker] text", null)]
    public void Constructor_ParsesErrorClassFromMessage(string message, string? expected)
    {
        var ex = new BricksNetException(message, sqlState: null, errorCode: 0, queryId: null);

        Assert.Equal(expected, ex.ErrorClass);
    }

    [Fact]
    public void ExplicitErrorClass_IsNotReparsed()
    {
        var ex = new BricksNetException("[BAD_REQUEST] Division by zero", null, 0, null, errorClass: null);

        Assert.Null(ex.ErrorClass);
    }

    [Theory]
    [InlineData("DELTA_CONCURRENT_APPEND", true)]
    [InlineData("DELTA_CONCURRENT_DELETE_READ", true)]
    [InlineData("DELTA_METADATA_CHANGED", true)]
    [InlineData("DELTA_CONCURRENT_APPEND.WITH_PARTITION_HINT", true)]
    [InlineData("TABLE_OR_VIEW_NOT_FOUND", false)]
    [InlineData(null, false)]
    public void IsTransient_ForDeltaConcurrencyConflicts(string? errorClass, bool expected)
    {
        var ex = new BricksNetException("failed", null, 0, null, errorClass);

        Assert.Equal(expected, ex.IsTransient);
    }

    [Theory]
    [InlineData(429, true)]
    [InlineData(503, true)]
    [InlineData(400, false)]
    [InlineData(0, false)]
    public void IsTransient_ForThrottlingAndUnavailability(int errorCode, bool expected)
    {
        var ex = new BricksNetException("failed", null, errorCode, null, errorClass: null);

        Assert.Equal(expected, ex.IsTransient);
    }

    [Fact]
    public void MessageOnlyConstructors_HaveNoErrorClass()
    {
        Assert.Null(new BricksNetException("[DELTA_CONCURRENT_APPEND] x").ErrorClass);
        Assert.False(new BricksNetException("x", new InvalidOperationException()).IsTransient);
    }
}
