using JobsPulse.Core.Model.Domain;

namespace JobsPulse.Core.Model.Infrastructure;

public sealed record StateCommit
{
    public required string SourceId { get; init; }
    public required string BoardId { get; init; }

    public required IReadOnlyList<Vacancy> Upserts { get; init; }
    public required IReadOnlyList<string> ClosedPostIds { get; init; }
    public required IReadOnlyList<OutboxItem> Notifications { get; init; }

    /// <summary>Hash of the filter set the upserted vacancies passed - stored per row to detect filter changes.</summary>
    public string? FilterHash { get; init; }

    /// <summary>Match layer: vacancies that pass a watchlist filter right now.</summary>
    public IReadOnlyList<WatchlistMatch> MatchUpserts { get; init; } = [];

    /// <summary>Match layer: vacancies that stopped passing a watchlist filter, or left the board.</summary>
    public IReadOnlyList<WatchlistMatchKey> MatchRemovals { get; init; } = [];

    public bool IsEmpty =>
        Upserts.Count == 0 &&
        ClosedPostIds.Count == 0 &&
        Notifications.Count == 0 &&
        MatchUpserts.Count == 0 &&
        MatchRemovals.Count == 0;
}
