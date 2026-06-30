using BricksNet.AdoNet;
using BricksNet.Backends.Thrift.Generated;
using Xunit;

namespace BricksNet.Backends.Thrift;

public sealed class ThriftStatusCheckerTests
{
    [Fact]
    public void EnsureSuccess_NullStatus_Throws()
    {
        var ex = Assert.Throws<BricksNetException>(() => ThriftStatusChecker.EnsureSuccess(null));
        Assert.Contains("without a status", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(nameof(TStatusCode.SuccessStatus))]
    [InlineData(nameof(TStatusCode.SuccessWithInfoStatus))]
    public void EnsureSuccess_SuccessCodes_DoNotThrow(string codeName)
    {
        var code = Enum.Parse<TStatusCode>(codeName);
        ThriftStatusChecker.EnsureSuccess(new TStatus { StatusCode = code });
    }

    [Fact]
    public void EnsureSuccess_StillExecuting_Throws_WithContext()
    {
        var status = new TStatus
        {
            StatusCode = TStatusCode.StillExecutingStatus,
            SqlState = "01000",
            ErrorCode = 7,
        };

        var ex = Assert.Throws<BricksNetException>(() => ThriftStatusChecker.EnsureSuccess(status, "q-1"));

        Assert.Equal("01000", ex.SqlState);
        Assert.Equal(7, ex.ErrorCode);
        Assert.Equal("q-1", ex.QueryId);
        Assert.Contains("still executing", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(nameof(TStatusCode.ErrorStatus))]
    [InlineData(nameof(TStatusCode.InvalidHandleStatus))]
    public void EnsureSuccess_ErrorCodes_Throw(string codeName)
    {
        var code = Enum.Parse<TStatusCode>(codeName);
        var status = new TStatus { StatusCode = code, ErrorCode = 500 };

        var ex = Assert.Throws<BricksNetException>(() => ThriftStatusChecker.EnsureSuccess(status, "q-2"));

        Assert.Equal(500, ex.ErrorCode);
        Assert.Equal("q-2", ex.QueryId);
    }

    [Fact]
    public void EnsureSuccess_PrefersDisplayMessage_ThenErrorMessage_ThenFallback()
    {
        var withDisplay = new TStatus
        {
            StatusCode = TStatusCode.ErrorStatus,
            DisplayMessage = "display",
            ErrorMessage = "error",
        };
        Assert.Equal("display", Assert.Throws<BricksNetException>(
            () => ThriftStatusChecker.EnsureSuccess(withDisplay)).Message);

        var withError = new TStatus
        {
            StatusCode = TStatusCode.ErrorStatus,
            DisplayMessage = "  ",
            ErrorMessage = "error-only",
        };
        Assert.Equal("error-only", Assert.Throws<BricksNetException>(
            () => ThriftStatusChecker.EnsureSuccess(withError)).Message);

        var withNeither = new TStatus { StatusCode = TStatusCode.ErrorStatus };
        Assert.Contains("reported an error", Assert.Throws<BricksNetException>(
            () => ThriftStatusChecker.EnsureSuccess(withNeither)).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnsureSuccess_NullErrorCode_DefaultsToZero()
    {
        var status = new TStatus { StatusCode = TStatusCode.ErrorStatus, ErrorCode = null };

        var ex = Assert.Throws<BricksNetException>(() => ThriftStatusChecker.EnsureSuccess(status));

        Assert.Equal(0, ex.ErrorCode);
    }
}

public sealed class ThriftHandleTests
{
    [Fact]
    public void ToId_NullOrEmpty_ReturnsEmptyString()
    {
        Assert.Equal(string.Empty, ThriftHandle.ToId(null));
        Assert.Equal(string.Empty, ThriftHandle.ToId(Array.Empty<byte>()));
    }

    [Fact]
    public void ToId_Bytes_ReturnsLowercaseHex()
    {
        var id = ThriftHandle.ToId(new byte[] { 0x0A, 0xBC, 0xDE });

        Assert.Equal("0abcde", id);
    }
}
