using System.Text.Json.Serialization;

namespace JobsPulse.Sources.SmartRecruiters.Models;

/// <summary>`GET /postings/{id}` - the only place with the job ad text and the public urls.</summary>
public sealed record PostingDetailDto
{
    [JsonPropertyName("id")] public required string Id { get; init; }

    /// <summary>The job behind the posting: several postings of one job share it.</summary>
    [JsonPropertyName("jobId")] public string? JobId { get; init; }

    [JsonPropertyName("postingUrl")] public string? PostingUrl { get; init; }

    [JsonPropertyName("applyUrl")] public string? ApplyUrl { get; init; }

    [JsonPropertyName("jobAd")] public PostingJobAdDto? JobAd { get; init; }
}

public sealed record PostingJobAdDto
{
    [JsonPropertyName("sections")] public PostingSectionsDto? Sections { get; init; }
}

public sealed record PostingSectionsDto
{
    [JsonPropertyName("companyDescription")] public PostingSectionDto? CompanyDescription { get; init; }

    [JsonPropertyName("jobDescription")] public PostingSectionDto? JobDescription { get; init; }

    [JsonPropertyName("qualifications")] public PostingSectionDto? Qualifications { get; init; }

    [JsonPropertyName("additionalInformation")] public PostingSectionDto? AdditionalInformation { get; init; }
}

public sealed record PostingSectionDto
{
    [JsonPropertyName("title")] public string? Title { get; init; }

    /// <summary>HTML.</summary>
    [JsonPropertyName("text")] public string? Text { get; init; }
}
