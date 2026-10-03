using System.Text;
using Microsoft.EntityFrameworkCore.Storage;

namespace BricksNet.EntityFrameworkCore.Storage.Internal;

/// <summary>
/// Databricks SQL syntax for identifiers, parameters and literals. Identifiers are delimited with
/// backticks (double quotes delimit string literals in Databricks), parameters use named
/// <c>:name</c> markers, and string literals use backslash escapes.
/// </summary>
/// <remarks>
/// This is an internal API that supports the EF Core infrastructure and is not subject to the
/// same compatibility standards as public APIs.
/// </remarks>
public class BricksNetSqlGenerationHelper : RelationalSqlGenerationHelper
{
    /// <summary>Initialises a new instance.</summary>
    public BricksNetSqlGenerationHelper(RelationalSqlGenerationHelperDependencies dependencies)
        : base(dependencies)
    {
    }

    /// <summary>
    /// Empty: each command is a single statement, and the SQL Statement Execution API rejects
    /// multi-statement text.
    /// </summary>
    public override string StatementTerminator => string.Empty;

    /// <summary>
    /// <c>;</c>: separates the statements of a generated script (<c>GenerateCreateScript</c>), so it
    /// can run as one Databricks SQL script. Commands sent by EF are never batched.
    /// </summary>
    public override string BatchTerminator => ";";

    /// <inheritdoc />
    public override string EscapeIdentifier(string identifier) =>
        identifier.Replace("`", "``", StringComparison.Ordinal);

    /// <inheritdoc />
    public override void EscapeIdentifier(StringBuilder builder, string identifier)
    {
        var start = builder.Length;
        builder.Append(identifier);
        builder.Replace("`", "``", start, identifier.Length);
    }

    /// <inheritdoc />
    public override string DelimitIdentifier(string identifier) => $"`{EscapeIdentifier(identifier)}`";

    /// <inheritdoc />
    public override void DelimitIdentifier(StringBuilder builder, string identifier)
    {
        builder.Append('`');
        EscapeIdentifier(builder, identifier);
        builder.Append('`');
    }

    /// <summary>Returns the named parameter marker for <paramref name="name"/>: <c>:name</c>.</summary>
    public override string GenerateParameterName(string name) => ":" + name;

    /// <summary>Appends the named parameter marker for <paramref name="name"/>: <c>:name</c>.</summary>
    public override void GenerateParameterName(StringBuilder builder, string name) => builder.Append(':').Append(name);

    /// <summary>
    /// Quotes <paramref name="value"/> as a Databricks string literal. Databricks processes
    /// backslash escapes in literals and treats <c>'a''b'</c> as two adjacent literals, so quotes
    /// and backslashes are escaped with a backslash rather than by doubling.
    /// </summary>
    public static string GenerateStringLiteral(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var builder = new StringBuilder(value.Length + 2);
        builder.Append('\'');
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\':
                    builder.Append(@"\\");
                    break;
                case '\'':
                    builder.Append(@"\'");
                    break;
                default:
                    builder.Append(c);
                    break;
            }
        }

        return builder.Append('\'').ToString();
    }
}
