namespace JobsPulse.Core.Pipeline;

/// <param name="ChangesSince">
/// The earliest previous traversal among the boards walked - the changes a cycle finds have piled up since then. Null
/// when every board was walked for the first time.
/// </param>
public readonly record struct CycleReport(
    int BoardsProcessed,
    int VacanciesFetched,
    int VacanciesStored,
    int VacanciesMatched,
    int Changes,
    int Failed,
    DateTimeOffset? ChangesSince = null)
{
    public static readonly CycleReport Empty = new(0, 0, 0, 0, 0, 0);

    public static CycleReport Aggregate(IReadOnlyList<BoardReport> boards) => new(
        boards.Count,
        boards.Sum(b => b.Fetched),
        boards.Sum(b => b.Stored),
        boards.Sum(b => b.Matched),
        boards.Sum(b => b.Changes),
        boards.Count(b => b.Failed));

    public static CycleReport Combine(IReadOnlyList<CycleReport> cycles) => new(
        cycles.Sum(c => c.BoardsProcessed),
        cycles.Sum(c => c.VacanciesFetched),
        cycles.Sum(c => c.VacanciesStored),
        cycles.Sum(c => c.VacanciesMatched),
        cycles.Sum(c => c.Changes),
        cycles.Sum(c => c.Failed),
        cycles.Min(c => c.ChangesSince));

    /// <summary>The earliest previous traversal of <paramref name="boardKeys"/>; boards never walked are skipped.</summary>
    public static DateTimeOffset? EarliestPoll(
        IEnumerable<string> boardKeys,
        IReadOnlyDictionary<string, DateTimeOffset> polled) =>
        boardKeys
            .Select(key => polled.TryGetValue(key, out var at) ? at : (DateTimeOffset?)null)
            .Min();
}
