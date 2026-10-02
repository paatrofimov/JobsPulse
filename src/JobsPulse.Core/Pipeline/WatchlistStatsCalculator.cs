using JobsPulse.Core.Model.Infrastructure;

namespace JobsPulse.Core.Pipeline;

/// <summary>
/// Pure function from the event history of a watchlist to its statistics for one period - no IO, no clock.
/// Whether a vacancy is open at a moment is decided by replaying the history up to it: the last event of a post
/// being <see cref="VacancyChangeKind.New"/> means it is open. Companies are counted by name, so one company watched
/// through two boards is one company.
/// </summary>
public static class WatchlistStatsCalculator
{
    public const int TopSize = 3;

    /// <param name="counts">
    /// Narrows the events counted inside the period - the changes of one run. Every event still counts for whether
    /// a vacancy is open.
    /// </param>
    public static WatchlistStats Compute(
        Watchlist watchlist,
        IReadOnlyList<WatchlistEvent> events,
        IReadOnlyDictionary<string, BoardActivity> activity,
        DateTimeOffset from,
        DateTimeOffset to,
        int days,
        Func<WatchlistEvent, bool>? counts = null)
    {
        // A stable sort: events stamped by one commit keep the order they were written in.
        events = [.. events.OrderBy(e => e.OccurredAt)];

        var names = CompanyNames(watchlist, events);

        var window = events
            .Where(e => e.OccurredAt >= from && e.OccurredAt <= to && (counts is null || counts(e)))
            .ToList();

        var openAtStart = CompaniesWithOpenPosts(events.Where(e => e.OccurredAt < from), names);
        var openAtEnd = CompaniesWithOpenPosts(events.Where(e => e.OccurredAt <= to), names);

        var openedByCompany = window
            .Where(e => e.Kind == VacancyChangeKind.New)
            .GroupBy(e => names(e.BoardKey), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var newCompanies = openedByCompany.Keys
            .Where(company => !openAtStart.Contains(company))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var emptiedCompanies = window
            .Where(e => e.Kind == VacancyChangeKind.Closed)
            .Select(e => names(e.BoardKey))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(company => !openAtEnd.Contains(company))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var topByOpened = openedByCompany
            .Select(x => new CompanyCount(x.Key, x.Value))
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.CompanyName, StringComparer.OrdinalIgnoreCase)
            .Take(TopSize)
            .ToList();

        // Activity is global to the board, so only the boards this watchlist still watches are ranked.
        var topByActivity = watchlist.Entries
            .Where(e => e.Enabled)
            .Select(e => e.BoardKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(activity.ContainsKey)
            .GroupBy(board => names(board), StringComparer.OrdinalIgnoreCase)
            .Select(g => new CompanyActivity(g.Key, Sum(g.Select(board => activity[board]))))
            .Where(x => x.Activity.Events > 0)
            .OrderByDescending(x => x.Activity.Events)
            .ThenBy(x => x.CompanyName, StringComparer.OrdinalIgnoreCase)
            .Take(TopSize)
            .ToList();

        return new WatchlistStats
        {
            WatchlistId = watchlist.Id,
            WatchlistName = watchlist.Name,
            Days = days,
            From = from,
            To = to,
            Opened = window.Count(e => e.Kind == VacancyChangeKind.New),
            Closed = window.Count(e => e.Kind == VacancyChangeKind.Closed),
            NewCompanies = newCompanies,
            EmptiedCompanies = emptiedCompanies,
            TopByActivity = topByActivity,
            TopByOpened = topByOpened
        };
    }

    /// <summary>Companies with at least one open post after replaying <paramref name="history"/> (oldest first).</summary>
    private static HashSet<string> CompaniesWithOpenPosts(
        IEnumerable<WatchlistEvent> history,
        Func<string, string> names)
    {
        var byBoard = new Dictionary<string, Dictionary<string, bool>>(StringComparer.OrdinalIgnoreCase);

        foreach (var e in history)
        {
            if (!byBoard.TryGetValue(e.BoardKey, out var posts))
                byBoard[e.BoardKey] = posts = new Dictionary<string, bool>(StringComparer.Ordinal);

            posts[e.PostId] = e.Kind == VacancyChangeKind.New;
        }

        return byBoard
            .Where(x => x.Value.Values.Any(open => open))
            .Select(x => names(x.Key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static BoardActivity Sum(IEnumerable<BoardActivity> boards)
    {
        var list = boards.ToList();

        return new BoardActivity(
            list.Sum(a => a.Opened),
            list.Sum(a => a.Changed),
            list.Sum(a => a.Closed),
            list[0].Months);
    }

    /// <summary>The current entry name wins; a board that left the watchlist keeps the name it was last reported under.</summary>
    private static Func<string, string> CompanyNames(Watchlist watchlist, IReadOnlyList<WatchlistEvent> events)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var e in events)
            names[e.BoardKey] = e.CompanyName;

        foreach (var entry in watchlist.Entries)
            names[entry.BoardKey] = entry.CompanyName;

        return board => names.TryGetValue(board, out var name) ? name : board;
    }
}
