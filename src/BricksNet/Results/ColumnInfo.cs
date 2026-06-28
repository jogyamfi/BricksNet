namespace BricksNet.Results;

/// <summary>Column descriptor returned as part of a query result schema.</summary>
public sealed record ColumnInfo(
    string Name,
    string TypeName,
    int Ordinal,
    bool IsNullable,
    int? Precision,
    int? Scale);
