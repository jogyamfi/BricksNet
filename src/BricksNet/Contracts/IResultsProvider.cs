namespace BricksNet.Contracts;

/// <summary>
/// Streaming row provider over a completed operation result.
/// Used internally by <see cref="IBricksNetOperation"/> to page through large result sets.
/// </summary>
internal interface IResultsProvider
{
    /// <summary><see langword="true"/> while the server reports additional rows available.</summary>
    bool HasMoreRows { get; }

    /// <summary>Fetches the next chunk of rows as loosely-typed dictionaries.</summary>
    Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> FetchNextChunkAsync(
        CancellationToken cancellationToken = default);
}
