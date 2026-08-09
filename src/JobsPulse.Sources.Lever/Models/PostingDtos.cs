using System.Text.Json.Serialization;

namespace JobsPulse.Sources.Lever.Models;

public sealed record PostingDto
{
    [JsonPropertyName("id")] public required string Id { get; init; }

    /// <summary>Posting title.</summary>
    [JsonPropertyName("text")] public string? Text { get; init; }

    [JsonPropertyName("hostedUrl")] public string? HostedUrl { get; init; }

    [JsonPropertyName("applyUrl")] public string? ApplyUrl { get; init; }

    /// <summary>Unix time in milliseconds.</summary>
    [JsonPropertyName("createdAt")] public long? CreatedAt { get; init; }

    [JsonPropertyName("categories")] public PostingCategoriesDto? Categories { get; init; }

    [JsonPropertyName("descriptionPlain")] public string? DescriptionPlain { get; init; }
}

public sealed record PostingCategoriesDto
{
    [JsonPropertyName("commitment")] public string? Commitment { get; init; }

    [JsonPropertyName("department")] public string? Department { get; init; }

    [JsonPropertyName("location")] public string? Location { get; init; }

    [JsonPropertyName("team")] public string? Team { get; init; }

    [JsonPropertyName("allLocations")] public List<string>? AllLocations { get; init; }
}
