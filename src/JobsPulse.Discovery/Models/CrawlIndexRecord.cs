namespace JobsPulse.Discovery.Models;

public sealed record CrawlIndexRecord
{
    public required string Url { get; init; }
}
