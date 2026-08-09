namespace JobsPulse.Discovery.Models;

public sealed record CrawlIndexRecord
{
    public required string Url { get; init; }

    public string? Timestamp { get; init; }

    public string? Status { get; init; }
}
