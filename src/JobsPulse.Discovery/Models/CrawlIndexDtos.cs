using System.Text.Json.Serialization;

namespace JobsPulse.Discovery.Models;

public sealed record CrawlCollectionDto
{
    [JsonPropertyName("id")] public string? Id { get; init; }

    [JsonPropertyName("name")] public string? Name { get; init; }

    [JsonPropertyName("cdx-api")] public string? CdxApi { get; init; }
}

public sealed record CrawlIndexPagesDto
{
    [JsonPropertyName("pages")] public int Pages { get; init; }

    [JsonPropertyName("pageSize")] public int PageSize { get; init; }
}

public sealed record CrawlIndexRecordDto
{
    [JsonPropertyName("url")] public string? Url { get; init; }

    [JsonPropertyName("timestamp")] public string? Timestamp { get; init; }

    [JsonPropertyName("status")] public string? Status { get; init; }
}
