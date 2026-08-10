namespace JobsPulse.Core.Pipeline;

public readonly record struct CycleReport(
    int BoardsProcessed,
    int VacanciesFetched,
    int VacanciesMatched,
    int Changes,
    int Failed)
{
    public static readonly CycleReport Empty = new(0, 0, 0, 0, 0);

    public static CycleReport Aggregate(IReadOnlyList<BoardReport> boards) => new(
        boards.Count,
        boards.Sum(b => b.Fetched),
        boards.Sum(b => b.Matched),
        boards.Sum(b => b.Changes),
        boards.Count(b => b.Failed));
}
