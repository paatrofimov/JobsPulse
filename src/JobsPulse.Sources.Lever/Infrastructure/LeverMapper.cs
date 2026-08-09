using JobsPulse.Core.Model.Domain;
using JobsPulse.Sources.Lever.Models;

namespace JobsPulse.Sources.Lever.Infrastructure;

public class LeverMapper(TimeProvider clock)
{
    public const string SourceId = "lever";

    public Vacancy ToVacancy(PostingDto dto, string boardId)
    {
        var categories = dto.Categories;

        return new Vacancy
        {
            SourceId = SourceId,
            BoardId = boardId,
            PostId = dto.Id,
            // Lever has no job-level id: one job posted to many locations is one posting with `allLocations`.
            GroupId = null,
            Title = dto.Text?.Trim() ?? dto.Id,
            Location = categories?.Location?.Trim(),
            Url = dto.HostedUrl ?? dto.ApplyUrl ?? $"https://jobs.lever.co/{boardId}/{dto.Id}",
            // The postings API exposes only the creation time.
            UpdatedAt = null,
            FirstPublishedAt = dto.CreatedAt is { } ms ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : null,
            FirstSeenAt = clock.GetUtcNow(),
            Offices = Offices(categories),
            Description = dto.DescriptionPlain
        };
    }

    private static IReadOnlyList<string> Offices(PostingCategoriesDto? categories)
    {
        if (categories?.AllLocations is { Count: > 0 } all)
            return all.Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim()).ToList();

        return categories?.Location is { } location && !string.IsNullOrWhiteSpace(location)
            ? [location.Trim()]
            : [];
    }
}
