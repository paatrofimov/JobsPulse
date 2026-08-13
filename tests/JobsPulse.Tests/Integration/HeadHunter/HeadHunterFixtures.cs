using System.Globalization;

namespace JobsPulse.Tests.Integration.HeadHunter;

/// <summary>Hand-written api payloads - shaped like the real ones, trimmed to the fields this source reads.</summary>
public static class HeadHunterFixtures
{
    public static string Employer(string id, string name, int openVacancies) =>
        $$"""
          {
            "id": "{{id}}",
            "name": "{{name}}",
            "type": "company",
            "site_url": "https://example.com",
            "alternate_url": "https://hh.ru/employer/{{id}}",
            "vacancies_url": "https://api.hh.ru/vacancies?employer_id={{id}}",
            "open_vacancies": {{openVacancies}},
            "area": { "id": "1", "name": "Москва" }
          }
          """;

    public static string EmployerSearch(params (string Id, string Name, int OpenVacancies)[] employers)
    {
        var items = employers.Select(e => Employer(e.Id, e.Name, e.OpenVacancies));

        return $$"""
                 {
                   "items": [{{string.Join(",", items)}}],
                   "found": {{employers.Length}},
                   "pages": 1,
                   "page": 0,
                   "per_page": 20
                 }
                 """;
    }

    public static string Vacancy(string id, string title, DateTimeOffset publishedAt) =>
        $$"""
          {
            "id": "{{id}}",
            "name": "{{title}}",
            "area": { "id": "1", "name": "Москва" },
            "address": { "city": "Москва", "street": "Льва Толстого" },
            "employer": { "id": "1740", "name": "Яндекс" },
            "snippet": { "requirement": "C#", "responsibility": "Писать код" },
            "url": "https://api.hh.ru/vacancies/{{id}}",
            "alternate_url": "https://hh.ru/vacancy/{{id}}",
            "published_at": "{{publishedAt.ToString("O", CultureInfo.InvariantCulture)}}",
            "created_at": "{{publishedAt.AddDays(-1).ToString("O", CultureInfo.InvariantCulture)}}",
            "archived": false
          }
          """;

    public static string VacancySearch(int found, int pages, params string[] vacancies) =>
        $$"""
          {
            "items": [{{string.Join(",", vacancies)}}],
            "found": {{found}},
            "pages": {{pages}},
            "page": 0,
            "per_page": {{Math.Max(1, vacancies.Length)}}
          }
          """;

    /// <summary>What the vacancy search answers for an employer id it does not know - HTTP 400, not a 404.</summary>
    public const string UnknownEmployer =
        """
        { "description": "employer_id", "errors": [ { "type": "bad_argument", "value": "employer_id" } ] }
        """;
}
