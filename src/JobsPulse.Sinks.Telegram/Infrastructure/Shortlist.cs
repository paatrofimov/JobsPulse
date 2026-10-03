using System.Text.RegularExpressions;
using JobsPulse.Core.Model.Domain;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Sinks.Telegram.Models;

namespace JobsPulse.Sinks.Telegram.Infrastructure;

/// <summary>
/// Where to apply first: the companies with the most matching vacancies inside a slice (a region or a freshness
/// window), and the most relevant vacancies of each. Pure, so it is testable on its own.
///
/// Only enabled companies take part - a disabled one is switched off by the user. Companies are counted by name, so
/// one company watched through two boards is one row. A vacancy is more relevant when its title hits more of the
/// wanted title words of the filter (every vacancy hits at least one when the filter has them), then when it is
/// fresher - the standard order of every listing.
/// </summary>
public static class Shortlist
{
    public const int CompanyCount = 10;

    public const int VacanciesPerCompany = 3;

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(200);

    public static IReadOnlyList<ShortlistCompany> Build(
        Watchlist watchlist,
        IReadOnlyList<Vacancy> vacancies,
        Func<Vacancy, bool> inSlice)
    {
        var entries = watchlist.Entries
            .Where(e => e.Enabled)
            .ToDictionary(e => e.BoardKey, StringComparer.OrdinalIgnoreCase);

        return vacancies
            .Where(inSlice)
            .Select(v => (Vacancy: v, Entry: entries.GetValueOrDefault($"{v.SourceId}/{v.BoardId}")))
            .Where(x => x.Entry is not null)
            .GroupBy(x => x.Entry!.CompanyName, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var ranked = g
                    .Select(x => x.Vacancy)
                    .OrderByDescending(v => Relevance(v, watchlist.Filter))
                    .ThenByDescending(v => Freshness(v) ?? DateTimeOffset.MinValue)
                    .ThenBy(v => v.Title, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var first = g.First().Entry!;

                return new
                {
                    Company = new ShortlistCompany(
                        g.Key,
                        first.Id,
                        g.Any(x => x.Entry!.IsWorked),
                        ranked.Count,
                        [.. ranked.Take(VacanciesPerCompany)]),
                    Best = Relevance(ranked[0], watchlist.Filter),
                    Freshest = ranked.Max(Freshness) ?? DateTimeOffset.MinValue
                };
            })
            .OrderByDescending(x => x.Company.Count)
            .ThenByDescending(x => x.Best)
            .ThenByDescending(x => x.Freshest)
            .ThenBy(x => x.Company.CompanyName, StringComparer.OrdinalIgnoreCase)
            .Take(CompanyCount)
            .Select(x => x.Company)
            .ToList();
    }

    /// <summary>Published inside the last <paramref name="days"/> days before <paramref name="now"/>.</summary>
    public static Func<Vacancy, bool> PublishedWithin(int days, DateTimeOffset now)
    {
        var since = now.AddDays(-days);

        return v => Freshness(v) is { } at && at >= since;
    }

    /// <summary>How many wanted title words of the filter the title hits.</summary>
    public static int Relevance(Vacancy vacancy, FilterSpec filter) =>
        filter.TitleAnyOf.Count(word => !string.IsNullOrWhiteSpace(word) && Hits(vacancy.Title, word, filter.MatchMode));

    /// <summary>
    /// The listing date (<see cref="VacancyPageBuilder.PublishedAt"/>), falling back to when the vacancy was first
    /// seen for a source that reports no date at all.
    /// </summary>
    private static DateTimeOffset? Freshness(Vacancy vacancy) =>
        VacancyPageBuilder.PublishedAt(vacancy) ?? vacancy.FirstSeenAt;

    private static bool Hits(string title, string word, FilterMatchMode mode)
    {
        switch (mode)
        {
            case FilterMatchMode.Exact:
                return string.Equals(title, word, StringComparison.OrdinalIgnoreCase);

            case FilterMatchMode.Regex:
                try
                {
                    return Regex.IsMatch(title, word, RegexOptions.IgnoreCase | RegexOptions.NonBacktracking, RegexTimeout);
                }
                catch (Exception ex) when (ex is ArgumentException or RegexMatchTimeoutException)
                {
                    return false;
                }

            default:
                return title.Contains(word, StringComparison.OrdinalIgnoreCase);
        }
    }
}
