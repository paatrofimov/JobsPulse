using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model;
using JobsPulse.Core.Pipeline;

namespace JobsPulse.Core.Tests;

internal static class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);

    public static Vacancy Vacancy(
        string externalId = "1",
        string title = "Senior Backend Engineer",
        string? location = "Remote, EU",
        string? groupId = "100",
        IReadOnlyList<string>? departments = null,
        DateTimeOffset? firstPublished = null) =>
        new()
        {
            SourceId = "greenhouse",
            BoardKey = "acme",
            ExternalId = externalId,
            GroupId = groupId,
            Title = title,
            Location = location,
            Url = $"https://job-boards.greenhouse.io/acme/jobs/{externalId}",
            UpdatedAt = Now,
            FirstPublished = firstPublished ?? Now.AddDays(-1),
            Departments = departments ?? ["Engineering"]
        };

    public static WatchEntry Entry(FilterSpec? filter = null) => new()
    {
        Id = "greenhouse:acme",
        Source = "greenhouse",
        Board = "acme",
        CompanyName = "Acme",
        Filter = filter,
        SeededAt = Now.AddDays(-1),
        SeededFilterHash = "seeded"
    };

    public static SeenVacancy Seen(Vacancy v, string? hash = null) => new()
    {
        ExternalId = v.ExternalId,
        ContentHash = hash ?? VacancyHasher.Compute(v),
        Title = v.Title,
        Location = v.Location,
        Url = v.Url,
        UpdatedAt = v.UpdatedAt,
        LastSeenAt = Now
    };
}
