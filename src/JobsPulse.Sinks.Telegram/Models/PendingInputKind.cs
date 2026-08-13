namespace JobsPulse.Sinks.Telegram.Models;

/// <summary>
/// What the bot is waiting for the user to type. Free text is asked for only where a button cannot do the job -
/// a name, a keyword list, a company query.
/// </summary>
public enum PendingInputKind
{
    None,
    WatchlistName,
    WatchlistRename,
    FilterKeywords,
    FilterExcluded,
    FilterLocations,
    CompanyQuery,

    /// <summary>The name of a company already in the watchlist - the entry point of «change a company».</summary>
    CompanyName
}
