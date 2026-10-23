using BricksNet.Contracts.Options;
using Xunit;

namespace BricksNet.Backends;

public sealed class BinaryParameterRewriterTests
{
    [Fact]
    public void Rewrite_NoBinaryParameters_ReturnsInputsUnchanged()
    {
        BricksNetParameter[] parameters = [new("id", 1)];

        var (sql, rewritten) = BinaryParameterRewriter.Rewrite("SELECT :id", parameters);

        Assert.Equal("SELECT :id", sql);
        Assert.Same(parameters, rewritten);
    }

    [Fact]
    public void Rewrite_ByteArray_SendsHexStringAndWrapsMarker()
    {
        var (sql, rewritten) = BinaryParameterRewriter.Rewrite(
            "INSERT INTO t VALUES (:id, :data)",
            [new BricksNetParameter("id", 1), new BricksNetParameter("data", new byte[] { 0x0A, 0xFF })]);

        Assert.Equal("INSERT INTO t VALUES (:id, unhex(:data))", sql);
        Assert.Equal(new BricksNetParameter("id", 1), rewritten![0]);
        Assert.Equal(new BricksNetParameter("data", "0AFF", "STRING"), rewritten[1]);
    }

    [Fact]
    public void Rewrite_TypedNull_BecomesStringNull()
    {
        var (sql, rewritten) = BinaryParameterRewriter.Rewrite(
            "UPDATE t SET data = :p0",
            [new BricksNetParameter("p0", null, "binary")]);

        Assert.Equal("UPDATE t SET data = unhex(:p0)", sql);
        Assert.Equal(new BricksNetParameter("p0", null, "STRING"), rewritten![0]);
    }

    [Fact]
    public void Rewrite_WrapsEveryMarker_ButNotPrefixesOfOtherNames()
    {
        var (sql, _) = BinaryParameterRewriter.Rewrite(
            "SELECT * FROM t WHERE a = :p1 OR b = :p10 OR c = :p1",
            [new BricksNetParameter("p1", new byte[] { 1 }), new BricksNetParameter("p10", 5)]);

        Assert.Equal("SELECT * FROM t WHERE a = unhex(:p1) OR b = :p10 OR c = unhex(:p1)", sql);
    }

    [Theory]
    [InlineData("SELECT ':b', \":b\", `:b` FROM t")]
    [InlineData("SELECT 'it\\'s :b' FROM t")]
    [InlineData("SELECT `a``:b` FROM t")]
    [InlineData("SELECT x::b -- :b\nFROM t /* :b */")]
    public void Rewrite_IgnoresMarkersInLiteralsIdentifiersCommentsAndCasts(string input)
    {
        var (sql, _) = BinaryParameterRewriter.Rewrite(input, [new BricksNetParameter("b", new byte[] { 1 })]);

        Assert.Equal(input, sql);
    }
}
