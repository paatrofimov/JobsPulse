using JobsPulse.Core.Model.Domain;

namespace JobsPulse.Core.Pipeline;

/// <param name="BoardMissing">The board answered 404 - the caller decides what to do with the dead board.</param>
/// <param name="Relevant">
/// Vacancies of the board that passed the storage filters, deduplicated. Returned so the registry cycle can test
/// them against the individual watchlist filters without fetching the board a second time.
/// </param>
public readonly record struct BoardProcessResult(
    BoardReport Report,
    bool BoardMissing,
    IReadOnlyList<Vacancy> Relevant)
{
    public static readonly BoardProcessResult Missing = new(BoardReport.Failure(), true, []);

    public static BoardProcessResult Failed() => new(BoardReport.Failure(), false, []);
}
