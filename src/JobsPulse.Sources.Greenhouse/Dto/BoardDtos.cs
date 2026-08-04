using System.Text.Json.Serialization;

namespace JobsPulse.Sources.Greenhouse.Dto;

// Формы ответов Job Board API. Держим отдельно от доменной модели:
// когда Greenhouse что-нибудь поменяет, чинить придётся только маппер.

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
    /// <summary>Id ПОСТА вакансии, не самой вакансии.</summary>
    [JsonPropertyName("id")] public long Id { get; set; }

    /// <summary>Id вакансии. null у prospect-постов — тогда дедуп по группе невозможен.</summary>
    [JsonPropertyName("internal_job_id")] public long? InternalJobId { get; set; }

    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("updated_at")] public DateTimeOffset? UpdatedAt { get; set; }
    [JsonPropertyName("first_published")] public DateTimeOffset? FirstPublished { get; set; }
    [JsonPropertyName("requisition_id")] public string? RequisitionId { get; set; }
    [JsonPropertyName("absolute_url")] public string AbsoluteUrl { get; set; } = "";
    [JsonPropertyName("location")] public LocationDto? Location { get; set; }

    /// <summary>Приходит только при content=true. HTML-энкоднутый — требует декодирования перед показом.</summary>
    [JsonPropertyName("content")] public string? Content { get; set; }

    [JsonPropertyName("departments")] public List<NamedDto>? Departments { get; set; }
    [JsonPropertyName("offices")] public List<NamedDto>? Offices { get; set; }
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

/// <summary>Ответ GET /boards/{token} — отсюда берём человекочитаемое имя компании.</summary>
public sealed class BoardDto
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("content")] public string? Content { get; set; }
}
