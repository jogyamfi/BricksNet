using System.Text;
using BricksNet.Contracts.Options;

namespace BricksNet.Backends;

/// <summary>
/// Works around Databricks rejecting <c>BINARY</c> parameter markers
/// (<c>INVALID_PARAMETER_MARKER_VALUE.INVALID_DATA_TYPE</c>): each binary parameter is sent as a
/// <c>STRING</c> holding the hex encoding of its bytes, and every <c>:name</c> marker that refers to
/// it is wrapped as <c>unhex(:name)</c> so the server sees a <c>BINARY</c> value again.
/// </summary>
/// <remarks>
/// A parameter is binary when its value is a <c>byte[]</c> or its type hint is <c>BINARY</c> (which
/// covers typed <c>NULL</c>s). Markers inside string literals, backtick identifiers and comments are
/// left alone, as is the <c>::</c> cast operator.
/// </remarks>
internal static class BinaryParameterRewriter
{
    public static (string Sql, IReadOnlyList<BricksNetParameter>? Parameters) Rewrite(
        string sql,
        IReadOnlyList<BricksNetParameter>? parameters)
    {
        ArgumentNullException.ThrowIfNull(sql);
        if (parameters is null || !parameters.Any(IsBinary))
        {
            return (sql, parameters);
        }

        var binaryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rewritten = new List<BricksNetParameter>(parameters.Count);
        foreach (var parameter in parameters)
        {
            if (!IsBinary(parameter))
            {
                rewritten.Add(parameter);
                continue;
            }

            binaryNames.Add(parameter.Name);
            var hex = parameter.Value is byte[] bytes ? Convert.ToHexString(bytes) : null;
            rewritten.Add(parameter with { Value = hex, TypeHint = "STRING" });
        }

        return (WrapMarkers(sql, binaryNames), rewritten);
    }

    private static bool IsBinary(BricksNetParameter parameter) =>
        parameter.Value is byte[]
        || string.Equals(parameter.TypeHint, "BINARY", StringComparison.OrdinalIgnoreCase);

    private static string WrapMarkers(string sql, HashSet<string> names)
    {
        var builder = new StringBuilder(sql.Length + (names.Count * 8));
        var i = 0;
        while (i < sql.Length)
        {
            var c = sql[i];
            var next = i + 1 < sql.Length ? sql[i + 1] : '\0';

            if (c is '\'' or '"' or '`')
            {
                var end = SkipQuoted(sql, i, c);
                builder.Append(sql, i, end - i);
                i = end;
            }
            else if (c == '-' && next == '-')
            {
                var end = sql.IndexOf('\n', i);
                end = end < 0 ? sql.Length : end;
                builder.Append(sql, i, end - i);
                i = end;
            }
            else if (c == '/' && next == '*')
            {
                var end = sql.IndexOf("*/", i + 2, StringComparison.Ordinal);
                end = end < 0 ? sql.Length : end + 2;
                builder.Append(sql, i, end - i);
                i = end;
            }
            else if (c == ':' && next == ':')
            {
                builder.Append("::");
                i += 2;
            }
            else if (c == ':' && IsNameStart(next))
            {
                var end = i + 1;
                while (end < sql.Length && IsNamePart(sql[end]))
                {
                    end++;
                }

                var name = sql.Substring(i + 1, end - i - 1);
                if (names.Contains(name))
                {
                    builder.Append("unhex(:").Append(name).Append(')');
                }
                else
                {
                    builder.Append(sql, i, end - i);
                }

                i = end;
            }
            else
            {
                builder.Append(c);
                i++;
            }
        }

        return builder.ToString();
    }

    // Returns the index just past the closing quote. Databricks string literals use backslash
    // escapes; backtick identifiers escape a backtick by doubling it.
    private static int SkipQuoted(string sql, int start, char quote)
    {
        var i = start + 1;
        while (i < sql.Length)
        {
            if (quote != '`' && sql[i] == '\\')
            {
                i += 2;
                continue;
            }

            if (sql[i] == quote)
            {
                if (quote == '`' && i + 1 < sql.Length && sql[i + 1] == '`')
                {
                    i += 2;
                    continue;
                }

                return i + 1;
            }

            i++;
        }

        return sql.Length;
    }

    private static bool IsNameStart(char c) => char.IsLetter(c) || c == '_';

    private static bool IsNamePart(char c) => char.IsLetterOrDigit(c) || c == '_';
}
