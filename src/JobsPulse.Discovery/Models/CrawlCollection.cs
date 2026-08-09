namespace JobsPulse.Discovery.Models;

/// <summary>One Common Crawl index collection, e.g. 'CC-MAIN-2025-30'.</summary>
public sealed record CrawlCollection
{
    public required string Id { get; init; }

    public required string CdxApiUrl { get; init; }

    public string? Name { get; init; }

    /// <summary>Crawl year parsed out of the id ('CC-MAIN-2025-30' → 2025); 0 when the id is not recognized.</summary>
    public int Year { get; init; }
}
