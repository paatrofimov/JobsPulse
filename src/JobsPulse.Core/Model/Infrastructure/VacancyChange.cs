using JobsPulse.Core.Model.Domain;

namespace JobsPulse.Core.Model.Infrastructure;

public sealed record VacancyChange
{
    public required VacancyChangeKind Kind { get; init; }

    public required Vacancy Vacancy { get; init; }

    /// <summary>The watchlist this change belongs to - one vacancy can produce one change per watchlist.</summary>
    public required long WatchlistId { get; init; }

    public required string WatchlistName { get; init; }

    public required string CompanyName { get; init; }

    public required string ContentHash { get; init; }
}
