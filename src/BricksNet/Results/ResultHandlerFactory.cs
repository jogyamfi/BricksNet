using BricksNet.Contracts;

namespace BricksNet.Results;

/// <summary>
/// Selects and constructs the appropriate <see cref="IResultsProvider"/> for a backend
/// <see cref="FetchResult"/> based on its <see cref="ResultFormat"/>.
/// </summary>
public static class ResultHandlerFactory
{
    private static readonly HttpClient SharedDownloadClient = new();

    /// <summary>
    /// Creates a results provider for <paramref name="result"/>. When
    /// <see cref="ResultHandlerOptions.MaxRows"/> is positive the provider is wrapped in a
    /// <see cref="ResultSlicer"/>.
    /// </summary>
    public static IResultsProvider Create(FetchResult result, ResultHandlerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        options ??= new ResultHandlerOptions();

        IResultsProvider provider = result.Format switch
        {
            ResultFormat.Arrow => new ArrowResultHandler(result.RawPayload),
            ResultFormat.JsonColumnar => new JsonResultHandler(result.RawPayload, result.Schema),
            ResultFormat.CloudFetch => new CloudFetchResultHandler(
                result.ExternalLinks ?? Array.Empty<ExternalLink>(),
                options.DownloadClient ?? SharedDownloadClient,
                options.CloudFetchConcurrentDownloads,
                options.Lz4Compressed),
            _ => throw new NotSupportedException(
                $"Result format '{result.Format}' is not supported."),
        };

        return options.MaxRows > 0 ? new ResultSlicer(provider, options.MaxRows) : provider;
    }
}
