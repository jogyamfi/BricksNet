using BricksNet.Contracts;

namespace BricksNet.Results;

/// <summary>
/// Decorates any <see cref="IResultsProvider"/> and caps the total number of rows surfaced across
/// chunk boundaries. A non-positive limit means "unlimited" and passes chunks through unchanged.
/// </summary>
public sealed class ResultSlicer : IResultsProvider
{
    private readonly IResultsProvider _inner;
    private readonly long _maxRows;
    private long _returnedRows;

    /// <summary>Creates a slicer over <paramref name="inner"/> enforcing <paramref name="maxRows"/>.</summary>
    public ResultSlicer(IResultsProvider inner, long maxRows)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _maxRows = maxRows;
    }

    /// <inheritdoc />
    public bool HasMoreRows =>
        (_maxRows <= 0 || _returnedRows < _maxRows) && _inner.HasMoreRows;

    /// <inheritdoc />
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> FetchNextChunkAsync(
        CancellationToken cancellationToken = default)
    {
        if (_maxRows > 0 && _returnedRows >= _maxRows)
        {
            return Array.Empty<IReadOnlyDictionary<string, object?>>();
        }

        var chunk = await _inner.FetchNextChunkAsync(cancellationToken).ConfigureAwait(false);
        if (_maxRows <= 0)
        {
            return chunk;
        }

        var remaining = _maxRows - _returnedRows;
        if (chunk.Count <= remaining)
        {
            _returnedRows += chunk.Count;
            return chunk;
        }

        var sliced = new List<IReadOnlyDictionary<string, object?>>((int)remaining);
        for (var i = 0; i < remaining; i++)
        {
            sliced.Add(chunk[i]);
        }

        _returnedRows += sliced.Count;
        return sliced;
    }
}
