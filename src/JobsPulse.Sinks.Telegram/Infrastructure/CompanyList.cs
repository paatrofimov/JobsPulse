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
    /// Source first, because that is what the list is grouped by; then active before disabled and manual before
    /// discovered, which is the order every other listing uses.
    /// </summary>
    public static List<WatchlistEntry> Order(IEnumerable<WatchlistEntry> entries) =>
    [
        .. entries
            .OrderBy(e => e.VacancySourceId, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(e => e.Enabled)
            .ThenBy(e => e.Origin)
            .ThenBy(e => e.CompanyName, StringComparer.OrdinalIgnoreCase)
    ];

    /// <summary>
    /// Groups an already ordered slice, preserving that order. A group larger than one page continues under a
    /// repeated header rather than being cut - the same way <see cref="VacancyPageBuilder"/> handles a long company.
    /// </summary>
    public static List<CompanyGroup> GroupBySource(IReadOnlyList<WatchlistEntry> ordered)
    {
        var groups = new List<CompanyGroup>();
        var current = new List<WatchlistEntry>();
        var sourceId = string.Empty;

        foreach (var entry in ordered)
        {
            if (current.Count > 0 && !string.Equals(entry.VacancySourceId, sourceId, StringComparison.OrdinalIgnoreCase))
            {
                groups.Add(new CompanyGroup(sourceId, current));
                current = [];
            }

            sourceId = entry.VacancySourceId;
            current.Add(entry);
        }

        if (current.Count > 0)
            groups.Add(new CompanyGroup(sourceId, current));

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
