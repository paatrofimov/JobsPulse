namespace JobsPulse.Core.Model.Infrastructure;

public sealed record WatchEntry
{
    // example: 'greenhouse:nebius'
    public required string Id { get; init; }

    public bool Enabled { get; init; } = true;

    public required string VacancySourceId { get; init; }

    public required string BoardId { get; init; }

    public required string CompanyName { get; init; }

    public int? IntervalMinutesOverride { get; init; }

    public FilterSpec? CustomFilter { get; init; }

    public DateTimeOffset? SeededAt { get; init; }

    public string? SeededFilterHash { get; init; }
}