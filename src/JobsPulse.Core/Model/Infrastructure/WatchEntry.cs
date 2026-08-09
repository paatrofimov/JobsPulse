using System.Text.Json.Serialization;

namespace JobsPulse.Core.Model.Infrastructure;

public sealed record WatchEntry
{
    // example: 'greenhouse:nebius'
    public required string Id { get; init; }

    public bool Enabled { get; init; } = true;

    [JsonPropertyName("Source")]
    public required string VacancySourceId { get; init; }

    [JsonPropertyName("Board")]
    public required string BoardId { get; init; }

    public required string CompanyName { get; init; }

    public int? IntervalMinutesOverride { get; init; }

    public FilterSpec? CustomFilter { get; init; }
}