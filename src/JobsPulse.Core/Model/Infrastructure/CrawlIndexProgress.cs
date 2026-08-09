namespace JobsPulse.Core.Model.Infrastructure;

/// <summary>Marks one (source, crawl index) pair as processed, so it is never walked twice.</summary>
public sealed record CrawlIndexProgress
{
    public required string SourceId { get; init; }

    public required string CollectionId { get; init; }

    public long RecordsSeen { get; init; }

    public int TokensFound { get; init; }

    public int BoardsAdded { get; init; }

    public DateTimeOffset ProcessedAt { get; init; }
}
