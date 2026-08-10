using JobsPulse.Core.Model.Domain;
using JobsPulse.Sources.SmartRecruiters.Models;

namespace JobsPulse.Sources.SmartRecruiters.Infrastructure;

public class SmartRecruitersMapper(TimeProvider clock)
{
    public const string SourceId = "smartrecruiters";

    public Vacancy ToVacancy(PostingDto dto, string boardId, PostingDetailDto? detail = null)
    {
        var location = Location(dto.Location);

        return new Vacancy
        {
            SourceId = SourceId,
            BoardId = boardId,
            PostId = dto.Id,
            // The job behind the posting is exposed by the detail endpoint only - without it there is nothing to group by.
            GroupId = detail?.JobId,
            Title = dto.Name?.Trim() ?? dto.RefNumber ?? dto.Id,
            Location = location,
            Url = detail?.PostingUrl ?? detail?.ApplyUrl ?? $"https://jobs.smartrecruiters.com/{boardId}/{dto.Id}",
            // The posting API reports the release date only, so change detection relies on the content hash.
            UpdatedAt = null,
            FirstPublishedAt = dto.ReleasedDate,
            FirstSeenAt = clock.GetUtcNow(),
            Offices = location is null ? [] : [location],
            Description = Description(detail)
        };
    }

    private static string? Location(PostingLocationDto? location)
    {
        if (location is null)
            return null;

        if (!string.IsNullOrWhiteSpace(location.FullLocation))
            return Decorate(location.FullLocation.Trim(), location);

        var parts = new[] { location.City, location.Region, location.Country }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Trim())
            .ToList();

        return parts.Count == 0
            ? location.Remote ? "Remote" : null
            : Decorate(string.Join(", ", parts), location);
    }

    private static string Decorate(string location, PostingLocationDto dto) => dto switch
    {
        { Remote: true } => $"{location} (remote)",
        { Hybrid: true } => $"{location} (hybrid)",
        _ => location
    };

    /// <summary>The job ad is split into sections; the filter needs one text, so they are concatenated in order.</summary>
    private static string? Description(PostingDetailDto? detail)
    {
        var sections = detail?.JobAd?.Sections;
        if (sections is null)
            return null;

        var texts = new[]
            {
                sections.JobDescription?.Text,
                sections.Qualifications?.Text,
                sections.AdditionalInformation?.Text,
                sections.CompanyDescription?.Text
            }
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();

        return texts.Count == 0 ? null : string.Join("\n", texts);
    }
}
