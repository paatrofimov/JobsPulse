using JobsPulse.Core.Model.Domain;
using JobsPulse.Sources.HeadHunter.Models;

namespace JobsPulse.Sources.HeadHunter.Infrastructure;

public class HeadHunterMapper(TimeProvider clock)
{
    public const string SourceId = "headhunter";

    /// <summary>
    /// A search item - plus the detail when the description was paid for - to the common vacancy model. The board is
    /// the employer, so `BoardId` is the employer id whatever the item says about itself.
    /// </summary>
    public Vacancy? ToVacancy(VacancyItemDto dto, string employerId, VacancyDetailDto? detail = null)
    {
        var postId = dto.Id?.Trim();

        // Without an id a vacancy cannot be followed across polls, so it is dropped rather than given one.
        if (string.IsNullOrWhiteSpace(postId))
            return null;

        var location = Location(dto);

        return new Vacancy
        {
            SourceId = SourceId,
            BoardId = employerId,
            PostId = postId,
            // The catalog exposes no requisition id: one job advertised in three cities is three unrelated vacancy
            // ids, and there is nothing that says they are one job. So nothing is grouped and every post passes
            // deduplication through, exactly as a SmartRecruiters posting without a detail request does.
            GroupId = null,
            Title = Title(dto, postId),
            Location = location,
            Offices = location is null ? [] : [location],
            Url = dto.AlternateUrl ?? $"https://hh.ru/vacancy/{postId}",
            // Republishing an ad bumps `published_at`, so it is the update stamp and not the first publication.
            UpdatedAt = dto.PublishedAt,
            FirstPublishedAt = dto.CreatedAt ?? dto.PublishedAt,
            FirstSeenAt = clock.GetUtcNow(),
            Description = Description(dto, detail)
        };
    }

    private static string Title(VacancyItemDto dto, string postId)
    {
        var name = dto.Name?.Trim();

        return string.IsNullOrWhiteSpace(name) ? postId : name;
    }

    /// <summary>
    /// The city of the address is the real place of work when there is one; `area` is the region the ad was published
    /// in and is all a remote vacancy has. A remote or hybrid format is marked the way the other sources mark it.
    /// </summary>
    private static string? Location(VacancyItemDto dto)
    {
        var place = Trimmed(dto.Address?.City) ?? Trimmed(dto.Area?.Name);
        var format = Format(dto);

        if (place is null)
            return format is null ? null : Capitalize(format);

        return format is null ? place : $"{place} ({format})";
    }

    private static string? Format(VacancyItemDto dto)
    {
        var formats = (dto.WorkFormat ?? [])
            .Select(f => f.Id?.ToUpperInvariant())
            .Where(id => id is not null)
            .ToList();

        if (formats.Contains("REMOTE"))
            return "remote";

        if (formats.Contains("HYBRID"))
            return "hybrid";

        return dto.Schedule?.Id switch
        {
            "remote" => "remote",
            _ => null
        };
    }

    /// <summary>
    /// The full text costs one request per vacancy; without it the search snippet is what there is, and it is enough
    /// for a keyword filter to work on.
    /// </summary>
    private static string? Description(VacancyItemDto dto, VacancyDetailDto? detail)
    {
        if (!string.IsNullOrWhiteSpace(detail?.Description))
            return detail!.Description!.Trim();

        var parts = new[] { dto.Snippet?.Responsibility, dto.Snippet?.Requirement }
            .Select(Trimmed)
            .Where(p => p is not null)
            .ToList();

        return parts.Count == 0 ? null : string.Join("\n", parts);
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Capitalize(string value) =>
        char.ToUpperInvariant(value[0]) + value[1..];
}
