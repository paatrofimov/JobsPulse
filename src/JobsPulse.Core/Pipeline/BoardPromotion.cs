namespace JobsPulse.Core.Pipeline;

/// <summary>One board promoted from the discovery registry into one watchlist.</summary>
/// <param name="Vacancies">How many matching vacancies were reported with the promotion.</param>
public readonly record struct BoardPromotion(
    string BoardKey,
    string CompanyName,
    long WatchlistId,
    string WatchlistName,
    int Vacancies);
