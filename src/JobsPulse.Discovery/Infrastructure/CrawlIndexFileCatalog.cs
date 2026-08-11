using System.Collections.Concurrent;
using System.IO.Compression;
using JobsPulse.Core.Infrastructure;
using JobsPulse.Discovery.Abstractions;
using JobsPulse.Discovery.Models;
using JobsPulse.Discovery.Options;
using Microsoft.Extensions.Options;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Discovery.Infrastructure;

/// <summary>
/// Reads `cc-index-table.paths.gz` - a couple of kilobytes listing every parquet file of a crawl. This is metadata,
/// not data: it is the only thing fetched over http, and it is what lets the queries touch the warc partition alone
/// instead of all three.
/// </summary>
public sealed class CrawlIndexFileCatalog(
    LoggingHttpClient http,
    IOptionsMonitor<DiscoveryOptions> options,
    ILog log) : ICrawlIndexFileCatalog
{
    public const string HttpClientName = "common-crawl-data";

    private readonly ILog ctxLog = log.ForContext<CrawlIndexFileCatalog>();

    // The listing of a published crawl never changes, so one read per process is enough.
    private readonly ConcurrentDictionary<string, IReadOnlyList<string>> cache = new(StringComparer.Ordinal);

    public async Task<IReadOnlyList<string>> GetFilesAsync(CrawlCollection collection, CancellationToken ct)
    {
        if (cache.TryGetValue(collection.Id, out var cached))
        {
            ctxLog.Debug("Serving {Count} parquet files of {Collection} from cache", cached.Count, collection.Id);
            return cached;
        }

        var opts = options.CurrentValue.Parquet;
        var url = new Uri(new Uri(opts.DataBaseUrl), opts.PathsFileTemplate.Replace("{crawl}", collection.Id)).ToString();

        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            ctxLog.Warn(
                "Parquet path listing of {Collection} is not readable: HTTP {Status} for {Url}",
                collection.Id, (int)response.StatusCode, url);

            return [];
        }

        var files = await ReadPathsAsync(response, opts, ct);

        if (files.Count == 0)
        {
            ctxLog.Warn("Parquet path listing of {Collection} holds no '{Subset}' files", collection.Id, opts.Subset);
            return [];
        }

        cache[collection.Id] = files;

        ctxLog.Info(
            "Collection {Collection} consists of {Count} '{Subset}' parquet files",
            collection.Id, files.Count, opts.Subset);

        return files;
    }

    private static async Task<IReadOnlyList<string>> ReadPathsAsync(
        HttpResponseMessage response,
        ParquetIndexOptions opts,
        CancellationToken ct)
    {
        var partition = $"subset={opts.Subset}/";
        var baseUri = new Uri(opts.DataBaseUrl);

        await using var body = await response.Content.ReadAsStreamAsync(ct);
        await using var unzipped = new GZipStream(body, CompressionMode.Decompress);
        using var reader = new StreamReader(unzipped);

        var files = new List<string>();

        while (await reader.ReadLineAsync(ct) is { } line)
        {
            var path = line.Trim();
            if (path.Length == 0 || !path.Contains(partition, StringComparison.Ordinal))
                continue;

            files.Add(new Uri(baseUri, path).ToString());
        }

        return files;
    }
}
