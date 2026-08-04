using JobsPulse.Core.Model;
using JobsPulse.Sources.Greenhouse.Dto;

namespace JobsPulse.Sources.Greenhouse;

/// <summary>Greenhouse DTO → доменная модель. Единственное место, где живёт знание о формате Greenhouse.</summary>
public static class GreenhouseMapper
{
    public const string SourceId = "greenhouse";

    public static Vacancy ToVacancy(JobDto dto, string boardKey)
    {
        var extra = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(dto.RequisitionId)) extra["requisition_id"] = dto.RequisitionId;

        return new Vacancy
        {
            SourceId = SourceId,
            BoardKey = boardKey,
            ExternalId = dto.Id.ToString(),
            GroupId = dto.InternalJobId?.ToString(),
            Title = dto.Title.Trim(),
            Location = dto.Location?.Name?.Trim(),
            Url = dto.AbsoluteUrl,
            UpdatedAt = dto.UpdatedAt ?? DateTimeOffset.MinValue,
            FirstPublished = dto.FirstPublished,
            Departments = Names(dto.Departments),
            Offices = Names(dto.Offices),
            DescriptionHtml = dto.Content,
            Extra = extra
        };
    }

    private static IReadOnlyList<string> Names(List<NamedDto>? items) =>
        items is null
            ? []
            : items.Select(i => i.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!.Trim()).ToList();
}
