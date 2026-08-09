using JobsPulse.Core.Model.Domain;

namespace JobsPulse.Core.Model.Infrastructure;

public sealed record SeenVacancySnapshot
{
    public required Vacancy Vacancy { get; init; }

    public DateTimeOffset? ClosedAt { get; init; }

    public bool IsOpen => ClosedAt is null;
}
