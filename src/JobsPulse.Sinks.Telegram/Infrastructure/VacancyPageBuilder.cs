using System.Net;
using System.Text;
using JobsPulse.Core.Model.Domain;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Sinks.Telegram.Infrastructure.Localization;

namespace JobsPulse.Sinks.Telegram.Infrastructure;

/// <summary>
/// Renders the vacancies of one watchlist as company blocks and packs them into as few screens as possible - the same
/// shape the delivered notifications have, so a browsed list and a pushed one read alike. Paging is by message size
/// rather than by a fixed count: a screen carries every vacancy that still fits, so most watchlists end up on one
/// page.
/// </summary>
public sealed class VacancyPageBuilder(TimeProvider clock)
{
    /// <summary>Telegram caps a message at 4096 visible characters; the rest is headroom for the paging label.</summary>
    private const int PageBudget = 3500;

    /// <summary>
    /// One entry per screen, in order. Empty when there is nothing to show. A company block longer than one screen is
    /// continued on the next one under the same header.
    /// </summary>
    public IReadOnlyList<string> Build(
        Watchlist watchlist,
        IReadOnlyList<Vacancy> vacancies,
        BotLanguage language)
    {
        var pages = new List<string>();
        var page = new StringBuilder();
        var used = 0;

        foreach (var company in Group(watchlist, vacancies))
        {
            var header = RenderHeader(company);
            var headerLength = VisibleLength(header);

            var pending = true;

            foreach (var vacancy in company.Vacancies)
            {
                var line = RenderVacancy(vacancy, language);
                var lineLength = VisibleLength(line);

                // The header is only worth its space when at least one of its vacancies follows it on the same page.
                var needed = (pending ? headerLength : 0) + lineLength;

                if (used + needed > PageBudget && page.Length > 0)
                {
                    pages.Add(page.ToString());
                    page.Clear();
                    used = 0;
                    pending = true;
                    needed = headerLength + lineLength;
                }

                if (pending)
                {
                    page.Append(header);
                    pending = false;
                }

                page.Append(line);
                used += needed;
            }
        }

        if (page.Length > 0)
            pages.Add(page.ToString());

        return pages;
    }

    /// <summary>
    /// One block per company. Manually added companies come first and discovered ones after them, exactly like the
    /// notifications; inside that, the company with the freshest vacancy leads.
    /// </summary>
    private static IEnumerable<CompanyBlock> Group(Watchlist watchlist, IReadOnlyList<Vacancy> vacancies) =>
        vacancies
            .GroupBy(v => (v.SourceId, v.BoardId))
            .Select(g =>
            {
                var entry = watchlist.FindEntry(g.Key.SourceId, g.Key.BoardId);

                return new CompanyBlock(
                    entry?.CompanyName ?? g.Key.BoardId,
                    entry?.Origin ?? BoardOrigin.Discovery,
                    entry?.IsWorked ?? false,
                    [.. g.OrderByDescending(PublishedAt).ThenBy(v => v.Title, StringComparer.OrdinalIgnoreCase)]);
            })
            .OrderBy(b => b.Origin)
            .ThenByDescending(b => b.Vacancies.Max(PublishedAt) ?? DateTimeOffset.MinValue)
            .ThenBy(b => b.Company, StringComparer.OrdinalIgnoreCase);

    /// <summary>The company glyphs are the ones the companies screen uses, so one list explains the other.</summary>
    private static string RenderHeader(CompanyBlock block)
    {
        var glyph = block.Worked ? "✅" : block.Origin == BoardOrigin.Discovery ? "🔎" : "🏢";

        return $"<h6>{glyph} {MessageFormatter.Escape(block.Company)} · {block.Vacancies.Count}</h6>";
    }

    private string RenderVacancy(Vacancy vacancy, BotLanguage language)
    {
        var title = $"<a href=\"{MessageFormatter.Escape(vacancy.Url)}\">"
                    + $"<b>{MessageFormatter.Escape(vacancy.Title)}</b></a>";

        var location = vacancy.Location
                       ?? (vacancy.Offices.Count > 0 ? string.Join(" · ", vacancy.Offices) : null)
                       ?? BotTexts.Get(TextKey.VacancyUnknownLocation, language);

        var published = PublishedAt(vacancy) ?? vacancy.FirstSeenAt;

        var date = published is { } value
            ? BotTexts.FormatDate(value, value.Year != clock.GetUtcNow().Year, language)
            : BotTexts.Get(TextKey.Nothing, language);

        return $"<p>{title}<br> {MessageFormatter.Escape(location)} · {date}</p>";
    }

    private static DateTimeOffset? PublishedAt(Vacancy vacancy) => vacancy.FirstPublishedAt ?? vacancy.UpdatedAt;

    /// <summary>
    /// What telegram counts against the message limit: the text the reader sees, without the markup and without the
    /// link targets, which are by far the longest part of a rendered vacancy.
    /// </summary>
    private static int VisibleLength(string html)
    {
        var text = new StringBuilder(html.Length);
        var inTag = false;

        foreach (var c in html)
        {
            switch (c)
            {
                case '<':
                    inTag = true;
                    break;

                case '>':
                    inTag = false;
                    break;

                default:
                    if (!inTag)
                        text.Append(c);

                    break;
            }
        }

        return WebUtility.HtmlDecode(text.ToString()).Length;
    }

    private sealed record CompanyBlock(
        string Company,
        BoardOrigin Origin,
        bool Worked,
        IReadOnlyList<Vacancy> Vacancies);
}
