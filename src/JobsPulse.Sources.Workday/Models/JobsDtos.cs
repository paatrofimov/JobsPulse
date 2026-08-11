using System.Text.Json.Serialization;

namespace JobsPulse.Sources.Workday.Models;

/// <summary>
/// One page of the careers site backend. Everything is nullable on purpose: this is an unversioned frontend contract,
/// and a field that disappears must cost one field, not the whole board.
/// </summary>
public sealed record JobsPageDto
{
    /// <summary>Trustworthy on the first page only, and capped by some tenants.</summary>
    [JsonPropertyName("total")] public int? Total { get; init; }

    [JsonPropertyName("jobPostings")] public List<JobPostingDto>? JobPostings { get; init; }
}

public sealed record JobPostingDto
{
    [JsonPropertyName("title")] public string? Title { get; init; }

    /// <summary>Starts with '/job/'. The only field a posting cannot be mapped without.</summary>
    [JsonPropertyName("externalPath")] public string? ExternalPath { get; init; }

    /// <summary>Sometimes a count instead of a place - '2 Locations'.</summary>
    [JsonPropertyName("locationsText")] public string? LocationsText { get; init; }

    /// <summary>Relative and human - 'Posted Today'. Deliberately not mapped.</summary>
    [JsonPropertyName("postedOn")] public string? PostedOn { get; init; }

    /// <summary>Per-site display configuration, not an id list - the first entry is usually the requisition id.</summary>
    [JsonPropertyName("bulletFields")] public List<string>? BulletFields { get; init; }

    [JsonPropertyName("timeType")] public string? TimeType { get; init; }
}

public sealed record JobDetailDto
{
    [JsonPropertyName("jobPostingInfo")] public JobPostingInfoDto? JobPostingInfo { get; init; }
}

public sealed record JobPostingInfoDto
{
    /// <summary>Workday id of the posting, opaque and stable.</summary>
    [JsonPropertyName("id")] public string? Id { get; init; }

    [JsonPropertyName("title")] public string? Title { get; init; }

    [JsonPropertyName("jobReqId")] public string? JobReqId { get; init; }

    [JsonPropertyName("jobPostingId")] public string? JobPostingId { get; init; }

    /// <summary>The only real date the public api exposes - ISO, date only.</summary>
    [JsonPropertyName("startDate")] public DateTimeOffset? StartDate { get; init; }

    [JsonPropertyName("jobDescription")] public string? JobDescription { get; init; }

    [JsonPropertyName("location")] public string? Location { get; init; }

    [JsonPropertyName("additionalLocations")] public List<string>? AdditionalLocations { get; init; }

    [JsonPropertyName("remoteType")] public string? RemoteType { get; init; }

    [JsonPropertyName("timeType")] public string? TimeType { get; init; }

    /// <summary>Canonical public url of the posting, as Workday itself reports it.</summary>
    [JsonPropertyName("externalUrl")] public string? ExternalUrl { get; init; }

    [JsonPropertyName("posted")] public bool? Posted { get; init; }
}
