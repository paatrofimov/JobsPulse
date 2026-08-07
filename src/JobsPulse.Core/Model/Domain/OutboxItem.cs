using JobsPulse.Core.Model.Infrastructure;

namespace JobsPulse.Core.Model.Domain;

public sealed record OutboxItem
{
    public long Id { get; init; }

    public required string DedupKey { get; init; }

    public required VacancyChangeKind ChangeKind { get; init; }
    public required string CompanyName { get; init; }
    public required Vacancy Vacancy { get; init; }

    public int Attempts { get; init; }
}