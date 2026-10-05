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
/// it, the busiest company first. Disabled companies are left out, as in the statistics.
/// </summary>
public sealed class DigestChangesScreen(
    IWatchlistDigestStorage digests,
    IWatchlistEventStorage events,
    WatchlistStatsService stats,
    WatchlistAccess access)
{
    /// <summary>The raw html one page may hold - well under the rich message limit, see <c>VacancyPageBuilder</c>.</summary>
    private const int PageBudget = 25_000;

    public async Task<ScreenView> RenderAsync(BotContext ctx, long digestId, int page, CancellationToken ct)
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

        var head = $"<h6>{BotTexts.Get(TextKey.DigestChangesTitle, ctx.Language, MessageFormatter.Escape(watchlist.Name))}</h6>"
                   + $"<p>{BotTexts.Get(TextKey.ChangesPeriod, ctx.Language, StatsFormatter.Period(digest.From, digest.To, ctx.Language))}</p>";

        var pages = Pages(Companies(computed, ctx.Language), Blocks(watchlist, changes, ctx.Language));

        if (pages.Count == 0)
        {
            return new ScreenView(
                head + $"<p>{BotTexts.Get(TextKey.DigestChangesEmpty, ctx.Language)}</p>",
                Keyboard(ctx, digestId, watchlist.Id, 0, 0));
        }

        var current = Math.Clamp(page, 0, pages.Count - 1);

        return new ScreenView(head + pages[current], Keyboard(ctx, digestId, watchlist.Id, current, pages.Count));
    }

    private static string? Companies(WatchlistStats stats, BotLanguage language)
    {
        if (stats.NewCompanies.Count == 0 && stats.EmptiedCompanies.Count == 0)
            return null;

        var sb = new StringBuilder("<p>");

        sb.Append(BotTexts.Get(TextKey.DigestNewCompanies, language, stats.NewCompanies.Count));
        if (stats.NewCompanies.Count > 0)
            sb.Append("<br>").Append(string.Join(", ", stats.NewCompanies.Select(MessageFormatter.Escape)));

        sb.Append("<br>").Append(BotTexts.Get(TextKey.DigestEmptiedCompanies, language, stats.EmptiedCompanies.Count));
        if (stats.EmptiedCompanies.Count > 0)
            sb.Append("<br>").Append(string.Join(", ", stats.EmptiedCompanies.Select(MessageFormatter.Escape)));

        return sb.Append("</p>").ToString();
    }

    /// <summary>One folded block per company, the company with the most changed vacancies first.</summary>
    private static List<string> Blocks(Watchlist watchlist, IReadOnlyList<WatchlistChange> changes, BotLanguage language)
    {
        var disabled = watchlist.Entries
            .Where(e => !e.Enabled)
            .Select(e => e.BoardKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // The current entry name wins, as in the statistics.
        var names = watchlist.Entries.ToDictionary(e => e.BoardKey, e => e.CompanyName, StringComparer.OrdinalIgnoreCase);

        return changes
            .Where(c => !disabled.Contains(c.Event.BoardKey))
            .GroupBy(c => (c.Event.BoardKey, c.Event.PostId))
            .Select(g => Line(g.ToList(), language))
            .GroupBy(x => names.GetValueOrDefault(x.Board, x.Company), StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var opened = g.Count(x => x.Kind == VacancyChangeKind.New);
                var summary = BotTexts.Get(TextKey.DigestCompanyRow, language, opened, g.Count() - opened);

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

    private static InlineKeyboardMarkup Keyboard(
        BotContext ctx,
        long digestId,
        long watchlistId,
        int page,
        int totalPages) =>
        new KeyboardBuilder(ctx.Language)
            .Paging(CallbackAction.DigestChanges, digestId, page, totalPages)
            .Build(CallbackAction.WatchlistOpen, watchlistId);
}
