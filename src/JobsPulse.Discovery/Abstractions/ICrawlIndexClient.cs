using JobsPulse.Discovery.Models;

namespace JobsPulse.Discovery.Abstractions;

/// <summary>
/// Generic crawl index reader - nothing here knows about a particular ATS.
/// </summary>
public interface ICrawlIndexClient
{
    /// <summary>All published collections, newest first. This is how the latest index is found without knowing its name.</summary>
    Task<IReadOnlyList<CrawlCollection>> GetCollectionsAsync(CancellationToken ct);

    Task<CrawlCollection?> GetLatestCollectionAsync(CancellationToken ct);

    Task<int> GetPageCountAsync(CrawlIndexQuery query, CancellationToken ct);

    IAsyncEnumerable<CrawlIndexRecord> StreamPageAsync(CrawlIndexQuery query, int page, CancellationToken ct);
}
