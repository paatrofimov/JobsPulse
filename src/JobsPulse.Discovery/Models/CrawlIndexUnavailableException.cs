namespace JobsPulse.Discovery.Models;

/// <summary>
/// A crawl index request never succeeded, so the caller has no data - not an empty answer, but no answer at all.
/// Callers must keep the collection pending instead of treating it as scanned.
/// </summary>
public sealed class CrawlIndexUnavailableException(string url, int attempts, string failure, Exception? inner = null)
    : Exception($"Crawl index request has failed after {attempts} attempt(s) ({failure}): {url}", inner)
{
    public string Url { get; } = url;

    public int Attempts { get; } = attempts;

    public string Failure { get; } = failure;
}
