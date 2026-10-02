namespace JobsPulse.Core.Model.Infrastructure;

/// <summary>
/// One change reported to one watchlist, kept after its notification is purged - the history the period statistics
/// are counted from. Only the changes that open or end a match are kept (<see cref="VacancyChangeKind.New"/>,
/// <see cref="VacancyChangeKind.Closed"/>, <see cref="VacancyChangeKind.AgedOut"/>,
/// <see cref="VacancyChangeKind.Filtered"/>): an update never changes whether a vacancy is open.
/// </summary>
public sealed record WatchlistEvent
{
    public required long WatchlistId { get; init; }

    public required string SourceId { get; init; }

    public required string BoardId { get; init; }

    public required string PostId { get; init; }

    /// <summary>The company name the change was reported under - the entry may be gone when the history is read.</summary>
    public required string CompanyName { get; init; }

    public required VacancyChangeKind Kind { get; init; }

    /// <summary>Where the vacancy is, as the board names it (its first office when it names no location).</summary>
    public string? Location { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>
    /// The `traversal_run` of the job that committed it - what tells the changes of one run from those of a job
    /// walking at the same time. Null outside a one-shot job and for history restored afterwards.
    /// </summary>
    public long? RunId { get; init; }

    public string BoardKey => $"{SourceId}/{BoardId}";

    /// <summary>The kinds worth keeping - see the type summary.</summary>
    public static bool IsTracked(VacancyChangeKind kind) =>
        kind is VacancyChangeKind.New or VacancyChangeKind.Closed or VacancyChangeKind.AgedOut
            or VacancyChangeKind.Filtered;
}
