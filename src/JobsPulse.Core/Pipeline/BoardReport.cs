namespace JobsPulse.Core.Pipeline;

/// <param name="Stored">Vacancies of the board that passed the storage filters and are kept in seen_vacancy.</param>
/// <param name="Matched">Match rows written for the board - one vacancy counts once per subscribed watchlist.</param>
public readonly record struct BoardReport(int Fetched, int Stored, int Matched, int Changes, bool Failed)
{
    public static BoardReport Failure() => new(0, 0, 0, 0, true);
}
