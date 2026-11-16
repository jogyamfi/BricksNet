using Apache.Arrow.Ipc;
using BricksNet.Contracts;

namespace BricksNet.Results;

/// <summary>
/// Downloads CloudFetch external result chunks and parses each as an Arrow IPC stream. Downloads
/// run in parallel up to a bounded degree, while rows are surfaced in ascending
/// <see cref="ExternalLink.ChunkIndex"/> order so the result set stays correctly ordered.
/// </summary>
internal sealed class CloudFetchResultHandler : IResultsProvider, IDisposable
{
    private readonly IReadOnlyList<ExternalLink> _links;
    private readonly HttpClient _httpClient;
    private readonly int _concurrency;
    private readonly bool _lz4Compressed;
    private readonly SemaphoreSlim _gate;
    private readonly CancellationTokenSource _cts = new();
    private readonly Queue<Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>>> _inflight = new();

    private int _nextToSchedule;
    private int _returnedChunks;

    /// <summary>Creates a handler over the supplied external links.</summary>
    /// <param name="links">The CloudFetch links to download.</param>
    /// <param name="httpClient">Client used for the (un-authenticated, pre-signed) downloads.</param>
    /// <param name="concurrentDownloads">Maximum number of parallel downloads (default 4).</param>
    /// <param name="lz4Compressed">Whether each chunk payload is LZ4 frame-compressed.</param>
    public CloudFetchResultHandler(
        IReadOnlyList<ExternalLink> links,
        HttpClient httpClient,
        int concurrentDownloads = 4,
        bool lz4Compressed = false)
    {
        ArgumentNullException.ThrowIfNull(links);
        ArgumentNullException.ThrowIfNull(httpClient);

        _links = links.OrderBy(link => link.ChunkIndex).ToList();
        _httpClient = httpClient;
        _concurrency = Math.Max(1, concurrentDownloads);
        _lz4Compressed = lz4Compressed;
        _gate = new SemaphoreSlim(_concurrency, _concurrency);
    }

    /// <inheritdoc />
    public bool HasMoreRows => _returnedChunks < _links.Count;

    /// <inheritdoc />
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> FetchNextChunkAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_returnedChunks >= _links.Count)
        {
            return Array.Empty<IReadOnlyDictionary<string, object?>>();
        }

        ScheduleDownloads();

        var task = _inflight.Dequeue();
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows;
        using (cancellationToken.Register(static state => ((CancellationTokenSource)state!).Cancel(), _cts))
        {
            rows = await task.ConfigureAwait(false);
        }

        _returnedChunks++;
        ScheduleDownloads();
        return rows;
    }

    private void ScheduleDownloads()
    {
        while (_inflight.Count < _concurrency && _nextToSchedule < _links.Count)
        {
            var link = _links[_nextToSchedule++];
            _inflight.Enqueue(DownloadAndParseAsync(link, _cts.Token));
        }
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> DownloadAndParseAsync(
        ExternalLink link,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (link.Expiration <= DateTimeOffset.UtcNow)
            {
                throw new CloudFetchLinkExpiredException(link.Uri, link.Expiration);
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, link.Uri);
            if (link.HttpHeaders is not null)
            {
                foreach (var header in link.HttpHeaders)
                {
                    request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var network = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            Stream arrowStream = network;
            if (_lz4Compressed)
            {
                // ArrowStreamReader reading straight from the LZ4 decoder stream sees no batches, so
                // decompress the whole chunk first. Memory stays bounded by the download concurrency.
                var decompressed = new MemoryStream();
                var lz4 = Lz4Decompressor.Decode(network);
                await using (lz4.ConfigureAwait(false))
                {
                    await lz4.CopyToAsync(decompressed, cancellationToken).ConfigureAwait(false);
                }

                decompressed.Position = 0;
                arrowStream = decompressed;
            }

            try
            {
                using var reader = new ArrowStreamReader(arrowStream, leaveOpen: true);
                var rows = new List<IReadOnlyDictionary<string, object?>>();
                while (true)
                {
                    using var batch = await reader
                        .ReadNextRecordBatchAsync(cancellationToken)
                        .ConfigureAwait(false);
                    if (batch is null)
                    {
                        break;
                    }

                    rows.AddRange(ArrowTypeConverter.ConvertBatch(batch));
                }

                return rows;
            }
            finally
            {
                // The LZ4 path already disposed the network stream; otherwise arrowStream is it.
                await arrowStream.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _cts.Cancel();
        while (_inflight.Count > 0)
        {
            // Observe any faults from in-flight downloads so they don't surface as unobserved.
            var pending = _inflight.Dequeue();
            _ = pending.ContinueWith(
                static t => t.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        }

        _cts.Dispose();
        _gate.Dispose();
    }
}
