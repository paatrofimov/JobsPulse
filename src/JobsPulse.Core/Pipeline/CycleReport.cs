namespace JobsPulse.Core.Pipeline;

public readonly record struct CycleReport(
    int BoardsProcessed,
    int VacanciesFetched,
    int VacanciesStored,
    int VacanciesMatched,
    int Changes,
    int Failed)
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
        cycles.Sum(c => c.Failed));
}
