using System.Text;
using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Core.Pipeline;
using JobsPulse.Sinks.Telegram.Infrastructure;
using JobsPulse.Sinks.Telegram.Infrastructure.Localization;
using JobsPulse.Sinks.Telegram.Models;
using Telegram.Bot.Types.ReplyMarkups;

namespace JobsPulse.Sinks.Telegram.Pipeline.Screens;

/// <summary>
/// Every change of one digest's period (`dg:<digest>:<page>`), the target of its «all changes» button: the new and
/// the emptied companies, then the vacancies by company - one line per vacancy with the last thing that happened to
/// it, the busiest company first, each company with what it opened, ended and its net. Narrowed by
/// <see cref="DigestChangesFilter"/> (`dgn` / `dgc`) to the vacancies that opened or ended, with only the new or only
/// the emptied companies. Disabled companies are left out, as in the statistics.
/// </summary>
public sealed class DigestChangesScreen(
    IWatchlistDigestStorage digests,
    IWatchlistEventStorage events,
    WatchlistStatsService stats,
    WatchlistAccess access)
{
    /// <summary>The raw html one page may hold - well under the rich message limit, see <c>VacancyPageBuilder</c>.</summary>
    private const int PageBudget = 25_000;

    public async Task<ScreenView> RenderAsync(
        BotContext ctx,
        long digestId,
        int page,
        DigestChangesFilter filter,
        CancellationToken ct)
    {
        var digest = await digests.GetAsync(digestId, ct);
        var watchlist = digest is null ? null : (await access.ResolveAsync(ctx, digest.WatchlistId, ct)).Watchlist;

        if (digest is null || watchlist is null)
        {
            return new ScreenView(
                $"<p>{BotTexts.Get(TextKey.WatchlistGone, ctx.Language)}</p>",
                new KeyboardBuilder(ctx.Language).Build(CallbackAction.Menu));
        }

        var computed = await stats.ComputeAsync(watchlist, digest.From, digest.To, ct);
        var changes = await events.LoadChangesAsync(watchlist.Id, digest.From, digest.To, ct);

        var title = filter switch
        {
            DigestChangesFilter.Opened => TextKey.DigestOpenedTitle,
            DigestChangesFilter.Closed => TextKey.DigestClosedTitle,
            _ => TextKey.DigestChangesTitle
        };

        var head = $"<h6>{BotTexts.Get(title, ctx.Language, MessageFormatter.Escape(watchlist.Name))}</h6>"
                   + $"<p>{BotTexts.Get(TextKey.ChangesPeriod, ctx.Language, StatsFormatter.Period(digest.From, digest.To, ctx.Language))}</p>";

        var pages = Pages(
            Companies(computed, filter, ctx.Language),
            Blocks(watchlist, computed, changes, filter, ctx.Language));

        if (pages.Count == 0)
        {
            return new ScreenView(
                head + $"<p>{BotTexts.Get(TextKey.DigestChangesEmpty, ctx.Language)}</p>",
                Keyboard(ctx, digestId, watchlist.Id, filter, 0, 0));
        }

        var current = Math.Clamp(page, 0, pages.Count - 1);

        return new ScreenView(
            head + pages[current],
            Keyboard(ctx, digestId, watchlist.Id, filter, current, pages.Count));
    }

    private static string? Companies(WatchlistStats stats, DigestChangesFilter filter, BotLanguage language)
    {
        var withNew = filter != DigestChangesFilter.Closed;
        var withEmptied = filter != DigestChangesFilter.Opened;

        if ((!withNew || stats.NewCompanies.Count == 0) && (!withEmptied || stats.EmptiedCompanies.Count == 0))
            return null;

        var lines = new List<string>();

        if (withNew)
            lines.Add(CompanyList(TextKey.DigestNewCompanies, stats.NewCompanies, language));

        if (withEmptied)
            lines.Add(CompanyList(TextKey.DigestEmptiedCompanies, stats.EmptiedCompanies, language));

        return $"<p>{string.Join("<br>", lines)}</p>";
    }

    private static string CompanyList(TextKey label, IReadOnlyList<string> companies, BotLanguage language)
    {
        var line = BotTexts.Get(label, language, companies.Count);

        return companies.Count == 0
            ? line
            : $"{line}<br>{string.Join(", ", companies.Select(MessageFormatter.Escape))}";
    }

    /// <summary>One folded block per company, the company with the most listed vacancies first.</summary>
    private static List<string> Blocks(
        Watchlist watchlist,
        WatchlistStats stats,
        IReadOnlyList<WatchlistChange> changes,
        DigestChangesFilter filter,
        BotLanguage language)
    {
        var disabled = watchlist.Entries
            .Where(e => !e.Enabled)
            .Select(e => e.BoardKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // The current entry name wins, as in the statistics.
        var names = watchlist.Entries.ToDictionary(e => e.BoardKey, e => e.CompanyName, StringComparer.OrdinalIgnoreCase);

        var totals = stats.ByCompany.ToDictionary(x => x.CompanyName, StringComparer.OrdinalIgnoreCase);

        return changes
            .Where(c => !disabled.Contains(c.Event.BoardKey))
            .GroupBy(c => (c.Event.BoardKey, c.Event.PostId))
            .Select(g => g.ToList())
            .Where(history => filter switch
            {
                DigestChangesFilter.Opened => history.Any(c => c.Event.Kind == VacancyChangeKind.New),
                DigestChangesFilter.Closed => history.Any(c => c.Event.Kind != VacancyChangeKind.New),
                _ => true
            })
            .Select(history => Line(history, language))
            .GroupBy(x => names.GetValueOrDefault(x.Board, x.Company), StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                // A company the statistics do not list moved nothing between open and not open.
                var total = totals.GetValueOrDefault(g.Key);
                var summary = BotTexts.Get(
                    TextKey.DigestCompanyRow,
                    language,
                    total?.Opened ?? 0,
                    (total?.Closed ?? 0) + (total?.Dropped ?? 0),
                    StatsFormatter.Delta(total?.Net ?? 0));

                return $"<details><summary><b>{MessageFormatter.Escape(g.Key)}</b> — {summary}</summary>"
                       + string.Join("<br>", g.Select(x => x.Html))
                       + "</details>";
            })
            .ToList();
    }

    /// <summary>The last change of one vacancy; one that opened and ended inside the period shows both glyphs.</summary>
    private static (string Board, string Company, VacancyChangeKind Kind, string Html) Line(
        List<WatchlistChange> history,
        BotLanguage language)
    {
        var last = history[^1];
        var e = last.Event;

        var glyph = MessageFormatter.KindGlyph(e.Kind);
        if (e.Kind != VacancyChangeKind.New && history.Any(c => c.Event.Kind == VacancyChangeKind.New))
            glyph = MessageFormatter.KindGlyph(VacancyChangeKind.New) + glyph;

        var title = MessageFormatter.Escape(last.Title ?? BotTexts.Get(TextKey.DigestUnknownTitle, language, e.PostId));
        var link = last.Url is { Length: > 0 } url
            ? $"<a href=\"{MessageFormatter.Escape(url)}\">{title}</a>"
            : title;
        var location = e.Location is { Length: > 0 } where ? $" · {MessageFormatter.Escape(where)}" : "";

        return (e.BoardKey, e.CompanyName, e.Kind, $"{glyph} {link}{location}");
    }

    /// <summary>The companies block opens the first page; a block never splits - one company is far below a page.</summary>
    private static List<string> Pages(string? companies, List<string> blocks)
    {
        var pages = new List<string>();
        var page = new StringBuilder(companies ?? "");

        foreach (var block in blocks)
        {
            if (page.Length > 0 && page.Length + block.Length > PageBudget)
            {
                pages.Add(page.ToString());
                page.Clear();
            }

            page.Append(block);
        }

        if (page.Length > 0)
            pages.Add(page.ToString());

        return pages;
    }

    /// <summary>The two filters not shown, then paging of the one shown.</summary>
    private static InlineKeyboardMarkup Keyboard(
        BotContext ctx,
        long digestId,
        long watchlistId,
        DigestChangesFilter filter,
        int page,
        int totalPages)
    {
        (DigestChangesFilter Filter, TextKey Label, CallbackAction Action)[] filters =
        [
            (DigestChangesFilter.All, TextKey.DigestAllChanges, CallbackAction.DigestChanges),
            (DigestChangesFilter.Opened, TextKey.DigestOpenedButton, CallbackAction.DigestOpened),
            (DigestChangesFilter.Closed, TextKey.DigestClosedButton, CallbackAction.DigestClosed)
        ];

        return new KeyboardBuilder(ctx.Language)
            .Modes([.. filters.Where(f => f.Filter != filter).Select(f => (f.Label, f.Action))], digestId)
            .Paging(filters.Single(f => f.Filter == filter).Action, digestId, page, totalPages)
            .Build(CallbackAction.WatchlistOpen, watchlistId);
    }
}
