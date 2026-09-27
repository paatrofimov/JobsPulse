namespace JobsPulse.Core.Model.Infrastructure;

public enum VacancyChangeKind
{
    Unknown = 0,
    New = 1,
    Updated = 2,
    Closed = 3,

    // Still open on the board, but older than the watchlist's PostedWithinDays
    AgedOut = 4,

    // Still open on the board, but no longer passes the watchlist's filter - the filter or the posting changed
    Filtered = 5,
}
