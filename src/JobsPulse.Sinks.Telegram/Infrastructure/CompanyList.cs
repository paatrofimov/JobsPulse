using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Sinks.Telegram.Models;

namespace JobsPulse.Sinks.Telegram.Infrastructure;

/// <summary>
/// Ordering, grouping and name lookup of a company list. Pure functions, kept out of the screen so the ordering of a
/// long list and the «which company did the user mean» rule can be read - and tested - on their own.
/// </summary>
public static class CompanyList
{
    /// <summary>
    /// Source first, because that is what the list is grouped by; then active before disabled, manual before
    /// discovered, more vacancy matches before less matches, which is the order every other listing uses.
    /// </summary>
    public static List<WatchlistEntry> Order(IEnumerable<WatchlistEntry> entries, IReadOnlyDictionary<string, int> matchesByBoard) =>
    [
        .. entries
            .OrderBy(e => e.VacancySourceId, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(e => e.Enabled)
            .ThenBy(e => e.Origin)
            .ThenByDescending(e => matchesByBoard.GetValueOrDefault(e.BoardKey, 0))
            .ThenBy(e => e.CompanyName, StringComparer.OrdinalIgnoreCase)
    ];

    /// <summary>
    /// Region first, because that is what the list is grouped by, and the regions come in their own order - Europe
    /// leads, see <see cref="LocationRegion"/>. Inside a region the ordering is the one every other listing uses.
    /// </summary>
    public static List<WatchlistEntry> OrderByRegion(
        IEnumerable<WatchlistEntry> entries,
        IReadOnlyDictionary<string, int> matchesByBoard,
        Func<WatchlistEntry, LocationRegion> regionOf) =>
    [
        .. entries
            .OrderBy(regionOf)
            .ThenByDescending(e => e.Enabled)
            .ThenBy(e => e.Origin)
            .ThenByDescending(e => matchesByBoard.GetValueOrDefault(e.BoardKey, 0))
            .ThenBy(e => e.CompanyName, StringComparer.OrdinalIgnoreCase)
    ];

    /// <summary>
    /// Groups an already ordered slice, preserving that order. A group larger than one page continues under a
    /// repeated header rather than being cut - the same way <see cref="VacancyPageBuilder"/> handles a long company.
    /// </summary>
    public static List<CompanyGroup> GroupBySource(IReadOnlyList<WatchlistEntry> ordered) =>
        Group(ordered, e => e.VacancySourceId);

    /// <summary>The same slicing by region; the label is already the name the reader sees.</summary>
    public static List<CompanyGroup> GroupByRegion(
        IReadOnlyList<WatchlistEntry> ordered,
        Func<WatchlistEntry, LocationRegion> regionOf,
        BotLanguage language) =>
        Group(
            ordered,
            e => $"{LocationRegions.Glyph(regionOf(e))} {LocationRegions.Name(regionOf(e), language)}");

    /// <summary>
    /// Consecutive entries sharing a label become one group. Consecutive rather than keyed, so the order of the slice
    /// decides everything and a group continued on the next page simply repeats its header.
    /// </summary>
    private static List<CompanyGroup> Group(
        IReadOnlyList<WatchlistEntry> ordered,
        Func<WatchlistEntry, string> labelOf)
    {
        var groups = new List<CompanyGroup>();
        var current = new List<WatchlistEntry>();
        var label = string.Empty;

        foreach (var entry in ordered)
        {
            var entryLabel = labelOf(entry);

            if (current.Count > 0 && !string.Equals(entryLabel, label, StringComparison.OrdinalIgnoreCase))
            {
                groups.Add(new CompanyGroup(label, current));
                current = [];
            }

            label = entryLabel;
            current.Add(entry);
        }

        if (current.Count > 0)
            groups.Add(new CompanyGroup(label, current));

        return groups;
    }

    /// <summary>
    /// The companies a typed name may mean. An exact match wins outright - a company whose name is contained in
    /// another one («Nebius» in «Nebius AI») must still be addressable by typing it in full.
    /// </summary>
    public static List<WatchlistEntry> Find(IReadOnlyList<WatchlistEntry> entries, string query)
    {
        query = query.Trim();

        if (query.Length == 0)
            return [];

        var exact = entries
            .Where(e => string.Equals(e.CompanyName, query, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (exact.Count > 0)
            return exact;

        return
        [
            .. entries
                .Where(e => e.CompanyName.Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderBy(e => e.CompanyName.Length)
                .ThenBy(e => e.CompanyName, StringComparer.OrdinalIgnoreCase)
        ];
    }
}