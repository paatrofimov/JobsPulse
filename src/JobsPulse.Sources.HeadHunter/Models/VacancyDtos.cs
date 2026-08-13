using System.Text.Json.Serialization;

namespace JobsPulse.Sources.HeadHunter.Models;

/// <summary>Response of `GET /vacancies` - the common search, filtered to one `employer_id`.</summary>
public sealed class VacancySearchDto
{
    [JsonPropertyName("items")] public List<VacancyItemDto>? Items { get; set; }

    /// <summary>How many vacancies the query matches - `JobCount` of a probe and the traversal's completeness check.</summary>
    [JsonPropertyName("found")] public int? Found { get; set; }

    [JsonPropertyName("pages")] public int? Pages { get; set; }
    [JsonPropertyName("page")] public int? Page { get; set; }
    [JsonPropertyName("per_page")] public int? PerPage { get; set; }
}

public sealed class VacancyItemDto
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("area")] public NamedDto? Area { get; set; }
    [JsonPropertyName("address")] public VacancyAddressDto? Address { get; set; }
    [JsonPropertyName("employer")] public EmployerItemDto? Employer { get; set; }
    [JsonPropertyName("snippet")] public VacancySnippetDto? Snippet { get; set; }
    [JsonPropertyName("schedule")] public NamedDto? Schedule { get; set; }
    [JsonPropertyName("work_format")] public List<NamedDto>? WorkFormat { get; set; }
    [JsonPropertyName("professional_roles")] public List<NamedDto>? ProfessionalRoles { get; set; }

    /// <summary>The human page of the vacancy - `https://hh.ru/vacancy/123`.</summary>
    [JsonPropertyName("alternate_url")] public string? AlternateUrl { get; set; }

    [JsonPropertyName("url")] public string? Url { get; set; }

    /// <summary>Bumped every time the employer republishes the ad, which is why it is not the first publication.</summary>
    [JsonPropertyName("published_at")] public DateTimeOffset? PublishedAt { get; set; }

    [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; set; }
    [JsonPropertyName("archived")] public bool? Archived { get; set; }
}

/// <summary>Response of `GET /vacancies/{id}` - only the full description is taken from it.</summary>
public sealed class VacancyDetailDto
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("alternate_url")] public string? AlternateUrl { get; set; }
    [JsonPropertyName("employer")] public EmployerItemDto? Employer { get; set; }
}

public sealed class VacancySnippetDto
{
    [JsonPropertyName("requirement")] public string? Requirement { get; set; }
    [JsonPropertyName("responsibility")] public string? Responsibility { get; set; }
}

public sealed class VacancyAddressDto
{
    [JsonPropertyName("city")] public string? City { get; set; }
    [JsonPropertyName("street")] public string? Street { get; set; }
    [JsonPropertyName("raw")] public string? Raw { get; set; }
}

public sealed class NamedDto
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
}
