using JobsPulse.Core.Model.Domain;

namespace JobsPulse.Core.Model.Infrastructure;

public sealed record VacancyChange
{
    public required VacancyChangeKind Kind { get; init; }

    public required Vacancy Vacancy { get; init; }

    public required string WatchEntryId { get; init; }

    public required string CompanyName { get; init; }

    public required string ContentHash { get; init; }
}