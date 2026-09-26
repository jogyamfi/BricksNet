namespace BricksNet.Results;

/// <summary>
/// Produces row-dictionary keys for a result schema. Rows are keyed by column name, so duplicate
/// names (e.g. <c>SELECT a.id, b.id</c>) would otherwise overwrite each other and drop a column.
/// Each repeat is renamed with a numeric suffix (<c>id</c>, <c>id_1</c>, <c>id_2</c>, ...) that
/// never collides with another column in the same result.
/// </summary>
internal static class ColumnNames
{
    public static string[] MakeUnique(IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        var result = new string[names.Count];
        var taken = new HashSet<string>(names, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < names.Count; i++)
        {
            var name = names[i];
            if (seen.Add(name))
            {
                result[i] = name;
                continue;
            }

            var suffix = 1;
            string candidate;
            do
            {
                candidate = $"{name}_{suffix++}";
            }
            while (taken.Contains(candidate));

            taken.Add(candidate);
            seen.Add(candidate);
            result[i] = candidate;
        }

        return result;
    }
}
