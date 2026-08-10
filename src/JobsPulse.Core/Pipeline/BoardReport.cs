namespace JobsPulse.Core.Pipeline;

/// <param name="Matched">Match rows written for the board - one vacancy counts once per subscribed watchlist.</param>
public readonly record struct BoardReport(int Fetched, int Matched, int Changes, bool Failed)
{
    public static BoardReport Failure() => new(0, 0, 0, true);
}
