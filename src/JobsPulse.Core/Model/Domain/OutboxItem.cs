using JobsPulse.Core.Model.Infrastructure;

namespace JobsPulse.Core.Model.Domain;

public sealed record OutboxItem
{
    public long Id { get; init; }

    public required string DedupKey { get; init; }

    public required VacancyChangeKind ChangeKind { get; init; }
    public required string CompanyName { get; init; }
    public required Vacancy Vacancy { get; init; }

    /// <summary>Which watchlist produced the notification. Null only for synthetic items (a state dump).</summary>
    public long? WatchlistId { get; init; }

    public string? WatchlistName { get; init; }

    /// <summary>
    /// The board came from discovery, not from a manual add. Denormalized for the same reason as
    /// <see cref="WatchlistName"/> - a delivered message must stay readable after the entry is gone.
    /// </summary>
    public bool Discovered { get; init; }

    /// <summary>
    /// When the change was detected and enqueued - not when it is delivered. The sink buckets a batch by this stamp,
    /// so everything one cycle found within a few minutes reads as one message instead of a wall of them.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }

    public int Attempts { get; init; }
}
