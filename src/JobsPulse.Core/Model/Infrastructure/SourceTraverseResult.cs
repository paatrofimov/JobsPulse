using JobsPulse.Core.Model.Domain;

namespace JobsPulse.Core.Model.Infrastructure;

public sealed record SourceTraverseResult
{
    public required bool IsComplete { get; init; }
    public required IReadOnlyList<Vacancy> Vacancies { get; init; }
    public string? Error { get; init; }

    /// HTTP 404
    public bool BoardMissing { get; init; }

    public static SourceTraverseResult Complete(IReadOnlyList<Vacancy> vacancies) =>
        new() { IsComplete = true, Vacancies = vacancies };

    public static SourceTraverseResult Failed(string error, bool boardMissing = false) =>
        new() { IsComplete = false, Vacancies = [], Error = error, BoardMissing = boardMissing };
}