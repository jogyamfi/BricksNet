using System.Globalization;
using BricksNet.Results;

namespace BricksNet.AdoNet;

/// <summary>
/// Recognises the summary result set Databricks returns for DML statements
/// (<c>num_affected_rows</c>, <c>num_inserted_rows</c>, ...) and extracts the affected-row count,
/// which Databricks reports as a result set rather than as a protocol-level count.
/// </summary>
internal static class DmlSummary
{
    /// <summary>The column carrying the total number of rows changed by the statement.</summary>
    public const string AffectedRowsColumn = "num_affected_rows";

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="columnNames"/> look like a DML summary:
    /// every column is a <c>num_*</c> counter and <see cref="AffectedRowsColumn"/> is among them.
    /// </summary>
    public static bool IsSummary(IReadOnlyCollection<string> columnNames) =>
        columnNames.Count > 0
        && columnNames.All(name => name.StartsWith("num_", StringComparison.OrdinalIgnoreCase))
        && columnNames.Contains(AffectedRowsColumn, StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns <see langword="true"/> when <paramref name="schema"/> describes a DML summary.</summary>
    public static bool IsSummary(IReadOnlyList<ColumnInfo> schema) =>
        IsSummary(schema.Select(column => column.Name).ToArray());

    /// <summary>
    /// Reads the affected-row count from a summary row, saturating at <see cref="int.MaxValue"/>.
    /// Returns <c>-1</c> when the row carries no usable count.
    /// </summary>
    public static int ReadAffectedRows(IReadOnlyDictionary<string, object?> row)
    {
        foreach (var (name, value) in row)
        {
            if (!string.Equals(name, AffectedRowsColumn, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return value switch
            {
                null => -1,
                string text when long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) =>
                    Saturate(parsed),
                IConvertible convertible => Saturate(convertible.ToInt64(CultureInfo.InvariantCulture)),
                _ => -1,
            };
        }

        return -1;
    }

    private static int Saturate(long value) => value > int.MaxValue ? int.MaxValue : (int)value;
}
