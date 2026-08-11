namespace JobsPulse.Discovery.Options;

/// <summary>`Discovery:Parquet` - the columnar index reader.</summary>
public sealed class ParquetIndexOptions
{
    /// <summary>Where both the parquet files and the per-crawl path listings live.</summary>
    public string DataBaseUrl { get; set; } = "https://data.commoncrawl.org/";

    /// <summary>Listing of every parquet file of one crawl; `{crawl}` is the collection id.</summary>
    public string PathsFileTemplate { get; set; } = "crawl-data/{crawl}/cc-index-table.paths.gz";

    /// <summary>Only the warc partition holds successful captures - robotstxt and crawldiagnostics are noise.</summary>
    public string Subset { get; set; } = "warc";

    /// <summary>
    /// How many parquet files one query covers. A crawl has ~300 of them; smaller batches mean more queries but a
    /// visible progress in the log and a failure that costs one batch instead of the whole collection.
    /// </summary>
    public int FilesPerQuery { get; set; } = 25;

    /// <summary>0 - no cap. Reads only the first N files of a collection, mostly for a smoke run.</summary>
    public int MaxFilesPerCollection { get; set; }

    /// <summary>Only successful captures are interesting - a 404 page proves nothing about a board.</summary>
    public int FetchStatus { get; set; } = 200;

    /// <summary>
    /// How many leading path segments are kept. The tail of a url is a posting id, and dropping it lets the index
    /// collapse millions of job pages into a few thousand distinct board urls before anything crosses the network.
    /// </summary>
    public int UrlPathSegments { get; set; } = 3;

    public int Retries { get; set; } = 3;

    public int RetryDelaySeconds { get; set; } = 15;

    public int MaxRetryDelaySeconds { get; set; } = 300;

    /// <summary>Failed batches tolerated inside one collection before it is abandoned and left pending.</summary>
    public int MaxBatchFailuresPerCollection { get; set; } = 3;

    /// <summary>Pause after every batch - the data host is shared infrastructure and throttles like the cdx one.</summary>
    public long PauseBetweenBatchesMsec { get; set; } = 500;

    /// <summary>When a collection could not be read from parquet, walk it with the http reader instead.</summary>
    public bool FallbackToHttp { get; set; } = true;

    public int QueryTimeoutSeconds { get; set; } = 900;

    /// <summary>DuckDB worker threads. Range requests, not cpu, are the bottleneck.</summary>
    public int Threads { get; set; } = 4;

    public int MemoryLimitMb { get; set; } = 2048;

    /// <summary>DuckDB's own http timeout, in milliseconds, and its own retry count for a range request.</summary>
    public int HttpTimeoutMsec { get; set; } = 120_000;

    public int HttpRetries { get; set; } = 3;

    public int HttpRetryWaitMsec { get; set; } = 2000;

    /// <summary>Where `httpfs` is installed. Null - DuckDB's default (`~/.duckdb/extensions`).</summary>
    public string? ExtensionDirectory { get; set; }
}
