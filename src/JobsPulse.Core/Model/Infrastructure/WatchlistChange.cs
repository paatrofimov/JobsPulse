namespace JobsPulse.Core.Model.Infrastructure;

/// <summary>
/// A history event with the vacancy it is about, as `seen_vacancy` knows it now. The title and the url are null when
/// the row is gone - a vacancy that stopped passing the storage filters is deleted, not closed.
/// </summary>
public sealed record WatchlistChange(WatchlistEvent Event, string? Title, string? Url);
