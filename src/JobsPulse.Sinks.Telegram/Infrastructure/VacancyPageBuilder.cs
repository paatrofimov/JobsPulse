using System.Net;
using System.Text;
using JobsPulse.Core.Model.Domain;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Sinks.Telegram.Infrastructure.Localization;
using JobsPulse.Sinks.Telegram.Models;

namespace JobsPulse.Sinks.Telegram.Infrastructure;

/// <summary>
/// Renders the vacancies of one watchlist as blocks - by company (the same shape the delivered notifications have),
/// by location, by month, or by company again but ordered by how hot the company is - and packs them into as few
/// screens as possible.
///
/// Every block is a collapsed <c>&lt;details&gt;</c> block, whatever its size: the page then opens as the list of
/// headers, and unfolding one is what shows its vacancies. That is also what lets a whole watchlist fit on one screen -
/// folded or not, the text still counts against the message limit, but <see cref="PageBudget"/> is the limit of a rich
/// message (the same one <see cref="MessageFormatter"/> sends notifications under), not the 4096 of a plain one.
/// </summary>
public sealed class VacancyPageBuilder(TimeProvider clock)
{
    /// <summary>
    /// What one screen may hold, counted as the text a reader sees. A rich message is not the 4096-character plain
    /// one - `MessageFormatter` has been splitting notifications at 30 000 for as long as it has existed - so a
    /// normal watchlist is one page and paging is the exception it was meant to be.
    /// </summary>
    private const int PageBudget = 30_000;

    /// <summary>
    /// The same limit applied to the raw html. Visible length is what telegram counts against the message limit, but
    /// the payload it *accepts* is the markup, and a vacancy line is mostly its link target: a page of long urls can
    /// be five times its visible size. Without this bound such a page is rejected by the api and the reader sees no
    /// screen at all - which is indistinguishable from an empty one.
    /// </summary>
    private const int RawBudget = 30_000;

    private const string DetailsClose = "</details>";

    /// <summary>
    /// The vacancies of the companies still being watched. A disabled company is one the user has switched off, so
    /// its vacancies have no business in the feed; a vacancy whose board is no longer in the watchlist at all is a
    /// match row the next polling cycle has not cleaned up yet and is dropped for the same reason.
    /// </summary>
    public static IReadOnlyList<Vacancy> OfActiveCompanies(
        Watchlist watchlist,
        IReadOnlyList<Vacancy> vacancies) =>
    [
        .. vacancies.Where(v => watchlist.FindEntry(v.SourceId, v.BoardId) is { Enabled: true })
    ];

    /// <summary>
    /// One entry per screen, in order. Empty when there is nothing to show. A block longer than one screen is
    /// continued on the next one under the same header.
    /// </summary>
    public IReadOnlyList<string> Build(
        Watchlist watchlist,
        IReadOnlyList<Vacancy> vacancies,
        BotLanguage language,
        VacancyGrouping grouping = VacancyGrouping.Company,
        IReadOnlyDictionary<string, BoardActivity>? activity = null)
    {
        var pages = new List<string>();
        var page = new StringBuilder();
        var used = 0;

        // A page holding only headers is an empty page to a reader, so a page is flushed on vacancy lines only.
        var lines = 0;

        foreach (var block in Blocks(watchlist, vacancies, language, grouping, activity))
        {
            if (block.Lines.Count == 0)
                continue;

            var header = RenderHeader(block);
            var headerLength = VisibleLength(header);

            var pending = true;

            foreach (var line in block.Lines)
            {
                var lineLength = VisibleLength(line);

                // The header is only worth its space when at least one of its vacancies follows it on the same page.
                var needed = (pending ? headerLength : 0) + lineLength;
                var needsRaw = (pending ? header.Length : 0) + line.Length;

                if ((used + needed > PageBudget || page.Length + needsRaw > RawBudget) && lines > 0)
                {
                    // A block continued on the next page is closed here, or the markup of the page would stay open.
                    if (!pending)
                        page.Append(DetailsClose);

                    pages.Add(page.ToString());
                    page.Clear();
                    used = 0;
                    lines = 0;
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
                lines++;
            }

            if (!pending)
                page.Append(DetailsClose);
        }

        if (lines > 0)
            pages.Add(page.ToString());

        return pages;
    }

    private IEnumerable<Block> Blocks(
        Watchlist watchlist,
        IReadOnlyList<Vacancy> vacancies,
        BotLanguage language,
        VacancyGrouping grouping,
        IReadOnlyDictionary<string, BoardActivity>? activity) =>
        grouping switch
        {
            VacancyGrouping.Location => ByRegion(watchlist, vacancies, language),
            VacancyGrouping.Month => ByMonth(watchlist, vacancies, language),
            VacancyGrouping.Activity => ByCompany(watchlist, vacancies, language, activity),
            _ => ByCompany(watchlist, vacancies, language, null)
        };

    /// <summary>
    /// One block per company. Without an activity table manually added companies come first and discovered ones
    /// after them, exactly like the notifications, and inside that the company with the freshest vacancy leads. With
    /// one, the hottest company leads instead and its rate is written into the header - that ordering is the whole
    /// point of the grouping, so origin and freshness step aside.
    /// </summary>
    private IEnumerable<Block> ByCompany(
        Watchlist watchlist,
        IReadOnlyList<Vacancy> vacancies,
        BotLanguage language,
        IReadOnlyDictionary<string, BoardActivity>? activity)
    {
        var blocks = vacancies
            .GroupBy(v => (v.SourceId, v.BoardId))
            .Select(g =>
            {
                var entry = watchlist.FindEntry(g.Key.SourceId, g.Key.BoardId);

                return new
                {
                    Company = entry?.CompanyName ?? g.Key.BoardId,
                    Origin = entry?.Origin ?? BoardOrigin.Discovery,
                    Worked = entry?.IsWorked ?? false,
                    Activity = activity is null
                        ? null
                        : ActivityRanks.Of(activity, $"{g.Key.SourceId}/{g.Key.BoardId}"),
                    Vacancies = Sorted(g)
                };
            });

        var ordered = activity is null
            ? blocks
                .OrderBy(b => b.Origin)
                .ThenByDescending(b => b.Vacancies.Max(PublishedAt) ?? DateTimeOffset.MinValue)
            : blocks
                .OrderBy(b => ActivityRanks.Rank(b.Activity!))
                .ThenByDescending(b => b.Activity!.PerMonth);

        return ordered
            .ThenBy(b => b.Company, StringComparer.OrdinalIgnoreCase)
            .Select(b => new Block(
                // The company glyphs are the ones the companies screen uses, so one list explains the other.
                b.Activity is { } rate
                    ? ActivityRanks.Glyph(ActivityRanks.Rank(rate))
                    : b.Worked ? "✅" : b.Origin == BoardOrigin.Discovery ? "🔎" : "🏢",
                MessageFormatter.Escape(b.Company),
                b.Vacancies.Count,
                [.. b.Vacancies.Select(v => RenderVacancy(v, null, language))],
                b.Activity is { } value ? ActivityRanks.Rate(value, language) : null));
    }

    /// <summary>
    /// One block per region, Europe first - see <see cref="LocationRegions"/>. The company is moved into the vacancy
    /// line, because a region block mixes companies and «which company is this» is the first question about a row.
    /// </summary>
    private IEnumerable<Block> ByRegion(
        Watchlist watchlist,
        IReadOnlyList<Vacancy> vacancies,
        BotLanguage language)
    {
        var byRegion = vacancies
            .GroupBy(LocationRegions.Of)
            .OrderBy(g => g.Key);

        foreach (var region in byRegion)
        {
            var lines = region
                .OrderBy(v => CompanyOf(watchlist, v), StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(PublishedAt)
                .Select(v => RenderVacancy(v, CompanyOf(watchlist, v), language))
                .ToList();

            yield return new Block(
                LocationRegions.Glyph(region.Key),
                MessageFormatter.Escape(LocationRegions.Name(region.Key, language)),
                lines.Count,
                lines);
        }
    }

    /// <summary>
    /// One block per month a vacancy appeared or last changed in, newest first - the same slicing the location
    /// grouping does, with the company again moved into the line for the same reason. A vacancy carrying no date at
    /// all is grouped under «date unknown» rather than dropped.
    /// </summary>
    private IEnumerable<Block> ByMonth(
        Watchlist watchlist,
        IReadOnlyList<Vacancy> vacancies,
        BotLanguage language)
    {
        var byMonth = vacancies
            .GroupBy(VacancyMonths.Of)
            .OrderByDescending(g => g.Key);

        foreach (var month in byMonth)
        {
            var lines = month
                .OrderByDescending(PublishedAt)
                .ThenBy(v => CompanyOf(watchlist, v), StringComparer.OrdinalIgnoreCase)
                .Select(v => RenderVacancy(v, CompanyOf(watchlist, v), language))
                .ToList();

            yield return new Block(
                VacancyMonths.Glyph(month.Key),
                MessageFormatter.Escape(VacancyMonths.Label(month.Key, language)),
                lines.Count,
                lines);
        }
    }

    /// <summary>A board that has left the watchlist still has vacancies in the feed - its id is the only name left.</summary>
    private static string CompanyOf(Watchlist watchlist, Vacancy vacancy) =>
        watchlist.FindEntry(vacancy.SourceId, vacancy.BoardId)?.CompanyName ?? vacancy.BoardId;

    /// <summary>Freshest first, exactly like a notification block.</summary>
    private static List<Vacancy> Sorted(IEnumerable<Vacancy> vacancies) =>
    [
        .. vacancies
            .OrderByDescending(PublishedAt)
            .ThenBy(v => v.Title, StringComparer.OrdinalIgnoreCase)
    ];

    /// <summary>
    /// The header lives in the <c>&lt;summary&gt;</c> - that line is what stays on screen while the block is
    /// collapsed, so it has to name the group and its size on its own.
    /// </summary>
    private static string RenderHeader(Block block)
    {
        var note = block.Note is null ? string.Empty : $" · {block.Note}";

        return $"<details><summary><b>{block.Glyph} {block.Title} · {block.Count}{note}</b></summary>";
    }

    private string RenderVacancy(Vacancy vacancy, string? company, BotLanguage language)
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

        var prefix = company is null ? string.Empty : $"{MessageFormatter.Escape(company)} · ";

        return $"<p>{title}<br> {prefix}{MessageFormatter.Escape(location)} · {date}</p>";
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

    /// <summary>
    /// One rendered group: the header parts and the vacancy lines, ready to be packed into pages. <paramref
    /// name="Note"/> is the extra the header carries when the grouping has something to say about the group itself -
    /// the activity rate of a company, and nothing otherwise.
    /// </summary>
    private sealed record Block(
        string Glyph,
        string Title,
        int Count,
        IReadOnlyList<string> Lines,
        string? Note = null);
}
