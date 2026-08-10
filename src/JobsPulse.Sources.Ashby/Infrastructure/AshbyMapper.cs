using JobsPulse.Core.Model.Domain;
using JobsPulse.Sources.Ashby.Models;

namespace JobsPulse.Sources.Ashby.Infrastructure;

public class AshbyMapper(TimeProvider clock)
{
    public const string SourceId = "ashby";

    public Vacancy ToVacancy(JobDto dto, string boardId)
    {
        return new Vacancy
        {
            SourceId = SourceId,
            BoardId = boardId,
            PostId = dto.Id,
            // One job is one posting: extra locations live inside it as `secondaryLocations`, not as separate posts.
            GroupId = null,
            Title = dto.Title?.Trim() ?? dto.Id,
            Location = Location(dto),
            Url = dto.JobUrl ?? dto.ApplyUrl ?? $"https://jobs.ashbyhq.com/{boardId}/{dto.Id}",
            // The posting API reports the publication date only, so change detection relies on the content hash.
            UpdatedAt = null,
            FirstPublishedAt = dto.PublishedAt,
            FirstSeenAt = clock.GetUtcNow(),
            Offices = Offices(dto),
            Description = dto.DescriptionPlain ?? dto.DescriptionHtml
        };
    }

    private static string? Location(JobDto dto)
    {
        var location = dto.Location?.Trim();

        if (string.IsNullOrWhiteSpace(location))
            return dto.IsRemote is true ? "Remote" : null;

        return dto.IsRemote is true ? $"{location} (remote)" : location;
    }

    /// <summary>The primary location plus every secondary one - a single job can be open in several places.</summary>
    private static IReadOnlyList<string> Offices(JobDto dto)
    {
        var offices = new List<string>();

        if (!string.IsNullOrWhiteSpace(dto.Location))
            offices.Add(dto.Location.Trim());

        foreach (var secondary in dto.SecondaryLocations ?? [])
        {
            var location = secondary.Location?.Trim();
            if (!string.IsNullOrWhiteSpace(location) && !offices.Contains(location, StringComparer.OrdinalIgnoreCase))
                offices.Add(location);
        }

        return offices;
    }
}
