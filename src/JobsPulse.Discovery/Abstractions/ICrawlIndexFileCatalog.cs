using JobsPulse.Discovery.Models;

namespace JobsPulse.Discovery.Abstractions;

/// <summary>
/// The metadata step of the columnar index: which parquet files one crawl consists of. Asking this first is what
/// makes it possible to query a minimal set of files instead of guessing paths or globbing a bucket.
/// </summary>
public interface ICrawlIndexFileCatalog
{
    /// <summary>Absolute http urls of the parquet files of the configured subset, in listing order.</summary>
    Task<IReadOnlyList<string>> GetFilesAsync(CrawlCollection collection, CancellationToken ct);
}
