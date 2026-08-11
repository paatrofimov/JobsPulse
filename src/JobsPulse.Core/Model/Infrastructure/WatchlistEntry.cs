namespace JobsPulse.Core.Model.Infrastructure;

/// <summary>One board inside one watchlist. The same board may belong to several watchlists.</summary>
public sealed record WatchlistEntry
{
    public long Id { get; init; }

    public long WatchlistId { get; init; }

    public required string VacancySourceId { get; init; }

    public required string BoardId { get; init; }

    public required string CompanyName { get; init; }

    /// <summary>Source-specific board parameters as json - see <see cref="BoardCandidate.Configuration"/>.</summary>
    public string? Configuration { get; init; }

    public bool Enabled { get; init; } = true;

    public string BoardKey => $"{VacancySourceId}/{BoardId}";
}
