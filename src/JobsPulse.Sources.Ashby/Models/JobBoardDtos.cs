using System.Text.Json.Serialization;

namespace JobsPulse.Sources.Ashby.Models;

/// <summary>The whole board in one response - the posting API has no paging and no per-job endpoint.</summary>
public sealed record JobBoardDto
{
    [JsonPropertyName("jobs")] public List<JobDto> Jobs { get; init; } = [];
}

public sealed record JobDto
{
    [JsonPropertyName("id")] public required string Id { get; init; }

    [JsonPropertyName("title")] public string? Title { get; init; }

    [JsonPropertyName("location")] public string? Location { get; init; }

    [JsonPropertyName("secondaryLocations")] public List<JobLocationDto>? SecondaryLocations { get; init; }

    /// <summary>ISO 8601, UTC.</summary>
    [JsonPropertyName("publishedAt")] public DateTimeOffset? PublishedAt { get; init; }

    /// <summary>An unlisted posting exists but must not be published.</summary>
    [JsonPropertyName("isListed")] public bool? IsListed { get; init; } = true;

    [JsonPropertyName("isRemote")] public bool? IsRemote { get; init; }

    [JsonPropertyName("jobUrl")] public string? JobUrl { get; init; }

    [JsonPropertyName("applyUrl")] public string? ApplyUrl { get; init; }

    [JsonPropertyName("descriptionPlain")] public string? DescriptionPlain { get; init; }

    [JsonPropertyName("descriptionHtml")] public string? DescriptionHtml { get; init; }
}

public sealed record JobLocationDto
{
    [JsonPropertyName("location")] public string? Location { get; init; }
}
