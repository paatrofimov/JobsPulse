using JobsPulse.Core.Model.Infrastructure;

namespace JobsPulse.Core.Pipeline;

/// <summary>
/// Pure function from the event history of a watchlist to its statistics for one period - no IO, no clock.
/// Whether a vacancy is open at a moment is decided by replaying the history up to it: the last event of a post
/// being <see cref="VacancyChangeKind.New"/> means it is open, and only an event that flips that is counted. Companies are counted by name, so one company watched
/// through two boards is one company. A disabled company is not counted anywhere - the user switched it off.
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

        var disabled = watchlist.Entries
            .Where(e => !e.Enabled)
            .Select(e => e.BoardKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var postsAtStart = OpenPosts(events.Where(e => e.OccurredAt < from), disabled);
        var postsAtEnd = OpenPosts(events.Where(e => e.OccurredAt <= to), disabled);

        var openAtStart = Companies(postsAtStart, names);
        var openAtEnd = Companies(postsAtEnd, names);

        var transitions = Transitions(events, disabled, from, to, counts);

        var opened = transitions.Where(t => t.Opened).ToList();
        var ended = transitions.Where(t => !t.Opened).ToList();

        var openedByCompany = opened
            .GroupBy(t => names(t.Event.BoardKey), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var newCompanies = openedByCompany.Keys
            .Where(company => !openAtStart.Contains(company))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var emptiedCompanies = ended
            .Select(t => names(t.Event.BoardKey))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(company => !openAtEnd.Contains(company))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var byCompany = transitions
            .GroupBy(t => names(t.Event.BoardKey), StringComparer.OrdinalIgnoreCase)
            .Select(g => new CompanyChanges
            {
                CompanyName = g.Key,
                Opened = g.Count(t => t.Opened),
                Closed = g.Count(t => !t.Opened && t.Event.Kind == VacancyChangeKind.Closed),
                Dropped = g.Count(t => !t.Opened && t.Event.Kind != VacancyChangeKind.Closed)
            })
            .OrderByDescending(x => x.Opened + x.Closed + x.Dropped)
            .ThenBy(x => x.CompanyName, StringComparer.OrdinalIgnoreCase)
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
            Opened = opened.Count,
            Closed = ended.Count(t => t.Event.Kind == VacancyChangeKind.Closed),
            Dropped = ended.Count(t => t.Event.Kind != VacancyChangeKind.Closed),
            OpenedAndEnded = ended.Count(t => t.EndsOpenedInPeriod),
            NewCompanies = newCompanies,
            EmptiedCompanies = emptiedCompanies,
            ByCompany = byCompany,
            TopByActivity = topByActivity,
            TopByOpened = topByOpened,
            OpenAtStart = new OpenCounts(postsAtStart.Count, openAtStart.Count),
            OpenAtEnd = new OpenCounts(postsAtEnd.Count, openAtEnd.Count)
        };
    }

    /// <summary>
    /// The counted events of the period that moved a vacancy between open and not open. A repeated event (a second
    /// close of a closed post) moves nothing and is skipped, which is what makes «open at start + opened − ended»
    /// equal «open at end».
    /// </summary>
    private static List<Transition> Transitions(
        IReadOnlyList<WatchlistEvent> events,
        HashSet<string> disabled,
        DateTimeOffset from,
        DateTimeOffset to,
        Func<WatchlistEvent, bool>? counts)
    {
        var open = new Dictionary<(string Board, string Post), bool>();
        var openedInPeriod = new HashSet<(string Board, string Post)>();
        var transitions = new List<Transition>();

        foreach (var e in events)
        {
            if (e.OccurredAt > to)
                break;

            var key = (e.BoardKey.ToLowerInvariant(), e.PostId);
            var wasOpen = open.GetValueOrDefault(key);
            var isOpen = e.Kind == VacancyChangeKind.New;

            open[key] = isOpen;

            if (wasOpen == isOpen || e.OccurredAt < from || disabled.Contains(key.Item1))
                continue;

            var counted = counts is null || counts(e);

            if (isOpen && counted)
                openedInPeriod.Add(key);

            var endsOpened = !isOpen && openedInPeriod.Remove(key);

            if (counted)
                transitions.Add(new Transition(e, isOpen, endsOpened));
        }

        return transitions;
    }

    /// <param name="EndsOpenedInPeriod">An end of a vacancy whose opening was counted in the same period.</param>
    private sealed record Transition(WatchlistEvent Event, bool Opened, bool EndsOpenedInPeriod);

    /// <summary>
    /// The posts open after replaying <paramref name="history"/> (oldest first), as (board, post), the disabled boards
    /// left out.
    /// </summary>
    private static HashSet<(string Board, string Post)> OpenPosts(
        IEnumerable<WatchlistEvent> history,
        HashSet<string> disabled)
    {
        var open = new Dictionary<(string Board, string Post), bool>();

        foreach (var e in history)
            open[(e.BoardKey.ToLowerInvariant(), e.PostId)] = e.Kind == VacancyChangeKind.New;

        return open
            .Where(x => x.Value && !disabled.Contains(x.Key.Board))
            .Select(x => x.Key)
            .ToHashSet();
    }

    private static HashSet<string> Companies(HashSet<(string Board, string Post)> posts, Func<string, string> names) =>
        posts
            .Select(x => names(x.Board))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

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
