namespace JobsPulse.Core.Model.Infrastructure;

public sealed record FilterSpec
{
    public IReadOnlyList<string> TitleAnyOf { get; init; } = [];
    public IReadOnlyList<string> TitleNoneOf { get; init; } = [];
    public IReadOnlyList<string> LocationAnyOf { get; init; } = [];
    public IReadOnlyList<string> LocationNoneOf { get; init; } = [];
    public IReadOnlyList<string> DepartmentAnyOf { get; init; } = [];

    public int? PostedWithinDays { get; init; }

    public FilterMatchMode MatchMode { get; init; } = FilterMatchMode.Substring;

    public static readonly FilterSpec MatchAll = new();

    public bool IsEmpty =>
        TitleAnyOf.Count == 0 && TitleNoneOf.Count == 0 &&
        LocationAnyOf.Count == 0 && LocationNoneOf.Count == 0 &&
        DepartmentAnyOf.Count == 0 && PostedWithinDays is null;
}