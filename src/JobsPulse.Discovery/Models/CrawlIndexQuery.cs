namespace JobsPulse.Discovery.Models;

public sealed record CrawlIndexQuery
{
    public required CrawlCollection Collection { get; init; }

    /// <summary>Url pattern in CDX syntax, e.g. 'boards.greenhouse.io/*'.</summary>
    public required string UrlPattern { get; init; }

    public int PageSize { get; init; } = 5;

    /// <summary>Only successful captures are interesting - a 404 page proves nothing about a board.</summary>
    public string? StatusFilter { get; init; } = "200";
}
