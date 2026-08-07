using JobsPulse.Core.Model.Domain;
using JobsPulse.Sources.Greenhouse.Models;

namespace JobsPulse.Sources.Greenhouse.Infrastructure;

public static class GreenhouseMapper
{
    public const string SourceId = "greenhouse";

    public static Vacancy ToVacancy(JobDto dto, string boardId)
    {
        return new Vacancy
        {
            SourceId = SourceId,
            BoardId = boardId,
            PostId = dto.PostId.ToString(),
            GroupId = dto.InternalJobId?.ToString(),
            Title = dto.Title.Trim(),
            Location = dto.Location?.Name?.Trim(),
            Url = dto.AbsoluteUrl,
            UpdatedAt = dto.UpdatedAt ?? DateTimeOffset.MinValue,
            FirstPublished = dto.FirstPublished,
            Departments = Names(dto.Departments),
            Offices = Names(dto.Offices),
            Description = dto.Description,
        };
    }

    private static IReadOnlyList<string> Names(List<NamedDto>? items) =>
        items is null
            ? []
            : items.Select(i => i.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!.Trim()).ToList();
}