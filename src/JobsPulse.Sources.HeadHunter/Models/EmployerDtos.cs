using System.Text.Json.Serialization;

namespace JobsPulse.Sources.HeadHunter.Models;

/// <summary>
/// Response of `GET /employers`. Every field is nullable: this is an unversioned public contract, and a field that
/// disappears must cost one field, not the whole lookup.
/// </summary>
public sealed class EmployerSearchDto
{
    [JsonPropertyName("items")] public List<EmployerItemDto>? Items { get; set; }
    [JsonPropertyName("found")] public int? Found { get; set; }
    [JsonPropertyName("pages")] public int? Pages { get; set; }
    [JsonPropertyName("page")] public int? Page { get; set; }
    [JsonPropertyName("per_page")] public int? PerPage { get; set; }
}

public sealed class EmployerItemDto
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }

    /// <summary>Api url of the employer - `https://api.hh.ru/employers/1740`.</summary>
    [JsonPropertyName("url")] public string? Url { get; set; }

    /// <summary>The human page - `https://hh.ru/employer/1740`, which is what a notification links to.</summary>
    [JsonPropertyName("alternate_url")] public string? AlternateUrl { get; set; }

    [JsonPropertyName("vacancies_url")] public string? VacanciesUrl { get; set; }
    [JsonPropertyName("open_vacancies")] public int? OpenVacancies { get; set; }
    [JsonPropertyName("area")] public NamedDto? Area { get; set; }
}

/// <summary>Response of `GET /employers/{id}` - the probe, and the only place the employer's own site is named.</summary>
public sealed class EmployerDetailDto
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("site_url")] public string? SiteUrl { get; set; }
    [JsonPropertyName("alternate_url")] public string? AlternateUrl { get; set; }
    [JsonPropertyName("vacancies_url")] public string? VacanciesUrl { get; set; }
    [JsonPropertyName("open_vacancies")] public int? OpenVacancies { get; set; }
    [JsonPropertyName("area")] public NamedDto? Area { get; set; }
}
