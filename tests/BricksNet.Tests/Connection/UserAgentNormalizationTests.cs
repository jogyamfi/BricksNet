using BricksNet.Connection;
using Xunit;

namespace BricksNet.Tests.Connection;

public sealed class UserAgentNormalizationTests
{
    [Theory]
    [InlineData("SimbaJDBCDriver 2.6.38",  "SimbaJDBCDriver/2.6.38")]
    [InlineData("SimbaODBCDriver 2.6.15",  "SimbaODBCDriver/2.6.15")]
    [InlineData("SomeDriver 1.0.0-beta",   "SomeDriver/1.0.0-beta")]
    [InlineData("SomeDriver 10.20.300",    "SomeDriver/10.20.300")]
    public void NormalizeUserAgent_SpaceSeparated_ConvertsToSlash(string input, string expected)
    {
        var result = HttpClientBuilderExtensions.NormalizeUserAgent(input, out var wasNormalized);

        Assert.Equal(expected, result);
        Assert.True(wasNormalized);
    }

    [Theory]
    [InlineData("SimbaJDBCDriver/2.6.38")]  // already correct
    [InlineData("BricksNet.dotnet/0.1.0-alpha")]
    [InlineData("MyDriver/1.0")]
    public void NormalizeUserAgent_SlashFormat_ReturnedUnchanged(string input)
    {
        var result = HttpClientBuilderExtensions.NormalizeUserAgent(input, out var wasNormalized);

        Assert.Equal(input, result);
        Assert.False(wasNormalized);
    }

    [Theory]
    [InlineData("justAProduct")]          // no version part
    [InlineData("a b c")]                 // two spaces — multi-token, not a product/version pair
    [InlineData("1driver 1.0")]           // starts with digit — not a valid product name
    public void NormalizeUserAgent_NonMatchingFormat_ReturnedUnchanged(string input)
    {
        var result = HttpClientBuilderExtensions.NormalizeUserAgent(input, out var wasNormalized);

        Assert.Equal(input, result);
        Assert.False(wasNormalized);
    }
}
