using System.Text;
using BricksNet.EntityFrameworkCore.Storage.Internal;
using Xunit;

namespace BricksNet.EntityFrameworkCore.Storage;

/// <summary>
/// String literals are the one place values are spliced into SQL text, so escaping must be exact:
/// every generated literal must decode back to the original value, and the quote that ends it
/// must be its last character.
/// </summary>
public sealed class StringLiteralFuzzTests
{
    private const string Alphabet = "ab'\\\"`;-\n\r\t\0 é漢😀%_*:@?";

    [Fact]
    public void RandomStrings_RoundTripThroughDatabricksLiteralDecoding()
    {
        var random = new Random(20261001);
        for (var i = 0; i < 5_000; i++)
        {
            var value = RandomString(random);

            var literal = BricksNetSqlGenerationHelper.GenerateStringLiteral(value);

            Assert.Equal(value, Decode(literal));
        }
    }

    [Theory]
    [InlineData("'; DROP TABLE t; --")]
    [InlineData("\\'; DROP TABLE t; --")]
    [InlineData("\\\\'")]
    [InlineData("'' OR ''=''")]
    public void InjectionAttempts_StayInsideTheLiteral(string value) =>
        Assert.Equal(value, Decode(BricksNetSqlGenerationHelper.GenerateStringLiteral(value)));

    private static string RandomString(Random random)
    {
        var length = random.Next(0, 24);
        var builder = new StringBuilder(length);
        for (var i = 0; i < length; i++)
        {
            builder.Append(Alphabet[random.Next(Alphabet.Length)]);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Decodes a single-quoted literal the way the Databricks SQL lexer does: a backslash escapes
    /// the next character and an unescaped quote ends the literal. Fails if the literal ends early.
    /// </summary>
    private static string Decode(string literal)
    {
        Assert.StartsWith("'", literal, StringComparison.Ordinal);
        var builder = new StringBuilder();
        for (var i = 1; i < literal.Length; i++)
        {
            var c = literal[i];
            if (c == '\\')
            {
                builder.Append(literal[++i]);
            }
            else if (c == '\'')
            {
                Assert.Equal(literal.Length - 1, i);
                return builder.ToString();
            }
            else
            {
                builder.Append(c);
            }
        }

        Assert.Fail($"Literal is not terminated: {literal}");
        return string.Empty;
    }
}
