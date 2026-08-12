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
    CompanyQuery
}
