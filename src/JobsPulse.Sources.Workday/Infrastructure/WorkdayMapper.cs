using System.Text.RegularExpressions;
using JobsPulse.Core.Model.Domain;
using JobsPulse.Sources.Workday.Models;

namespace JobsPulse.Sources.Workday.Infrastructure;

public partial class WorkdayMapper(TimeProvider clock)
{
    public const string SourceId = "workday";

    /// <summary>
    /// <paramref name="detail"/> is null whenever the description budget did not cover this posting - the list
    /// response alone is enough for identity, title and url.
    /// </summary>
    public Vacancy ToVacancy(
        JobPostingDto dto,
        WorkdayBoardConfig config,
        string externalPath,
        JobPostingInfoDto? detail)
    {
        var postId = WorkdayPostingIdentity.PostId(externalPath);

        return new Vacancy
        {
            SourceId = SourceId,
            BoardId = config.BoardId,
            PostId = postId,
            GroupId = WorkdayPostingIdentity.GroupId(externalPath, detail?.JobReqId),
            Title = Title(dto, detail) ?? postId,
            Location = Location(dto, detail),
            Offices = Offices(dto, detail),
            // Always the careers site, never the backend the vacancy was read from.
            Url = detail?.ExternalUrl ?? config.JobUrl(externalPath),
            // The list carries a relative 'Posted 13 Days Ago' only, which would rewrite itself every day - the
            // date is taken from the detail endpoint or left unknown, and change detection uses the content hash.
            UpdatedAt = null,
            FirstPublishedAt = detail?.StartDate,
            FirstSeenAt = clock.GetUtcNow(),
            Description = detail?.JobDescription
        };
    }

    private static string? Title(JobPostingDto dto, JobPostingInfoDto? detail)
    {
        var title = dto.Title?.Trim();

        return string.IsNullOrWhiteSpace(title) ? detail?.Title?.Trim() : title;
    }

    private static string? Location(JobPostingDto dto, JobPostingInfoDto? detail)
    {
        var detailLocation = detail?.Location?.Trim();
        if (!string.IsNullOrWhiteSpace(detailLocation))
            return Remote(detailLocation, detail);

        var listed = dto.LocationsText?.Trim();

        // '2 Locations' is a count of places, not a place - it must not end up as the location of a vacancy.
        if (string.IsNullOrWhiteSpace(listed) || LocationCountPattern().IsMatch(listed))
            return null;

        return Remote(listed, detail);
    }

    private static string Remote(string location, JobPostingInfoDto? detail)
    {
        var remote = detail?.RemoteType?.Trim();

        return string.IsNullOrWhiteSpace(remote) ? location : $"{location} ({remote})";
    }

    /// <summary>Every place a posting is open in - the detail endpoint is the only source that lists them all.</summary>
    private static IReadOnlyList<string> Offices(JobPostingDto dto, JobPostingInfoDto? detail)
    {
        var offices = new List<string>();

        void Add(string? office)
        {
            var trimmed = office?.Trim();

            if (!string.IsNullOrWhiteSpace(trimmed) && !offices.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
                offices.Add(trimmed);
        }

        Add(detail?.Location);

        foreach (var additional in detail?.AdditionalLocations ?? [])
            Add(additional);

        // Without a detail the list gives one place at most, and only when it is not a count.
        if (offices.Count == 0 && dto.LocationsText is { } listed && !LocationCountPattern().IsMatch(listed.Trim()))
            Add(listed);

        return offices;
    }

    /// <summary>'2 Locations', '6 Locations' - what the list shows instead of a place for a multi-site posting.</summary>
    [GeneratedRegex(@"^\d+\s+Locations?$", RegexOptions.IgnoreCase)]
    private static partial Regex LocationCountPattern();
}
