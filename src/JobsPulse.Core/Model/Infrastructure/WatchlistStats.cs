namespace JobsPulse.Core.Model.Infrastructure;

/// <summary>What happened to one watchlist during the last <see cref="Days"/> days - see <c>WatchlistStatsCalculator</c>.</summary>
public sealed record WatchlistStats
{
    public required long WatchlistId { get; init; }

    public required string WatchlistName { get; init; }

    public required int Days { get; init; }

    public required DateTimeOffset From { get; init; }

    public required DateTimeOffset To { get; init; }

    /// <summary>Vacancies that started matching the watchlist.</summary>
    public required int Opened { get; init; }

    /// <summary>Matching vacancies that left their board.</summary>
    public required int Closed { get; init; }

    /// <summary>Companies that had no open matching vacancy when the period began and got one during it.</summary>
    public required IReadOnlyList<string> NewCompanies { get; init; }

    /// <summary>Companies that closed something during the period and have no open matching vacancy left.</summary>
    public required IReadOnlyList<string> EmptiedCompanies { get; init; }

    /// <summary>The busiest boards of the watchlist by <see cref="BoardActivity.Events"/>, global to the board.</summary>
    public required IReadOnlyList<CompanyActivity> TopByActivity { get; init; }

    /// <summary>Companies with the most vacancies opened in the period.</summary>
    public required IReadOnlyList<CompanyCount> TopByOpened { get; init; }

    /// <summary>What was open when the period began - the «before» of a digest.</summary>
    public OpenCounts? OpenAtStart { get; init; }

    /// <summary>What is open when the period ends - the «after» of a digest.</summary>
    public OpenCounts? OpenAtEnd { get; init; }
}
