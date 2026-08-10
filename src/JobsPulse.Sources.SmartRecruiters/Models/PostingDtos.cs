using System.Text.Json.Serialization;

namespace JobsPulse.Sources.SmartRecruiters.Models;

/// <summary>Every list of the posting API is wrapped into this - paging is `offset` + `limit` against `totalFound`.</summary>
public sealed record PostingListDto
{
    [JsonPropertyName("offset")] public int Offset { get; init; }

    [JsonPropertyName("limit")] public int Limit { get; init; }

    [JsonPropertyName("totalFound")] public int TotalFound { get; init; }

    [JsonPropertyName("content")] public List<PostingDto> Content { get; init; } = [];
}

/// <summary>A list item. Descriptions and the job id live in the posting detail only.</summary>
public sealed record PostingDto
{
    [JsonPropertyName("id")] public required string Id { get; init; }

    /// <summary>Posting title.</summary>
    [JsonPropertyName("name")] public string? Name { get; init; }

    [JsonPropertyName("refNumber")] public string? RefNumber { get; init; }

    /// <summary>ISO 8601, UTC.</summary>
    [JsonPropertyName("releasedDate")] public DateTimeOffset? ReleasedDate { get; init; }

    [JsonPropertyName("company")] public PostingCompanyDto? Company { get; init; }

    [JsonPropertyName("location")] public PostingLocationDto? Location { get; init; }
}

public sealed record PostingCompanyDto
{
    [JsonPropertyName("identifier")] public string? Identifier { get; init; }

    [JsonPropertyName("name")] public string? Name { get; init; }
}

public sealed record PostingLocationDto
{
    [JsonPropertyName("city")] public string? City { get; init; }

    [JsonPropertyName("region")] public string? Region { get; init; }

    [JsonPropertyName("country")] public string? Country { get; init; }

    [JsonPropertyName("remote")] public bool Remote { get; init; }

    [JsonPropertyName("hybrid")] public bool Hybrid { get; init; }

    /// <summary>Pre-composed «city, region, country» - not always set.</summary>
    [JsonPropertyName("fullLocation")] public string? FullLocation { get; init; }
}
