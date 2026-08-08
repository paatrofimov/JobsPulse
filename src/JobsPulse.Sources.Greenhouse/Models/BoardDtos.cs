using System.Text.Json.Serialization;

namespace JobsPulse.Sources.Greenhouse.Models;

public sealed class JobListResponse
{
    [JsonPropertyName("jobs")] public List<JobDto> Jobs { get; set; } = [];
    [JsonPropertyName("meta")] public MetaDto? Meta { get; set; }
}

public sealed class MetaDto
{
    [JsonPropertyName("total")] public int Total { get; set; }
}

public sealed class JobDto
{
    [JsonPropertyName("id")] public long PostId { get; set; }

    // vacancy id. null for prospect-posts
    [JsonPropertyName("internal_job_id")] public long? InternalJobId { get; set; }

    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("updated_at")] public DateTimeOffset? UpdatedAt { get; set; }
    [JsonPropertyName("first_published")] public DateTimeOffset? FirstPublished { get; set; }
    [JsonPropertyName("absolute_url")] public string AbsoluteUrl { get; set; } = "";
    [JsonPropertyName("location")] public LocationDto? Location { get; set; }

    [JsonPropertyName("offices")] public List<NamedDto>? Offices { get; set; }
    
    [JsonPropertyName("content")] public string? Description { get; set; }
}

public sealed class LocationDto
{
    [JsonPropertyName("name")] public string? Name { get; set; }
}

public sealed class NamedDto
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
}

// Reponse GET /boards/{token}
public sealed class BoardDto
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("content")] public string? Content { get; set; }
}