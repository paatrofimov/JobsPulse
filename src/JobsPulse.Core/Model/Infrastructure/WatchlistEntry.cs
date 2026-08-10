namespace JobsPulse.Core.Model.Infrastructure;

/// <summary>One board inside one watchlist. The same board may belong to several watchlists.</summary>
public sealed record WatchlistEntry
{
    public long Id { get; init; }

    public long WatchlistId { get; init; }

    public required string VacancySourceId { get; init; }

    public required string BoardId { get; init; }

    public required string CompanyName { get; init; }

    public bool Enabled { get; init; } = true;

    public string BoardKey => $"{VacancySourceId}/{BoardId}";
}
