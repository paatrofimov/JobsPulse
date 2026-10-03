using System.Text;
using JobsPulse.Core.Abstractions;
using JobsPulse.Sinks.Telegram.Infrastructure;
using JobsPulse.Sinks.Telegram.Infrastructure.Localization;
using JobsPulse.Sinks.Telegram.Models;
using Telegram.Bot.Types.ReplyMarkups;

namespace JobsPulse.Sinks.Telegram.Pipeline.Screens;

/// <summary>
/// The matching vacancies of one company, freshest first and unfolded - the target of a company link in a digest,
/// a run report, the statistics screen and the shortlist. A company watched through several boards shows all of
/// them; the disabled ones are left out unless the link points at one.
/// </summary>
public sealed class CompanyVacanciesScreen(
    WatchlistAccess access,
    IStateStore stateStore,
    VacancyPageBuilder pages)
{
    private const int MaxVacancies = 500;

    public async Task<ScreenView> RenderAsync(BotContext ctx, long entryId, int page, CancellationToken ct)
    {
        var resolved = await access.ResolveEntryAsync(ctx, entryId, ct);
        if (resolved is not { Watchlist: { } watchlist, Entry: { } entry })
        {
            return new ScreenView(
                $"<p>{BotTexts.Get(TextKey.WatchlistGone, ctx.Language)}</p>",
                new KeyboardBuilder(ctx.Language).Build(CallbackAction.Menu));
        }

        var boards = watchlist.Entries
            .Where(e => e.Id == entry.Id
                        || (e.Enabled && string.Equals(e.CompanyName, entry.CompanyName, StringComparison.OrdinalIgnoreCase)))
            .Select(e => e.BoardKey)
            .ToList();

        var vacancies = await stateStore.LoadMatchedVacanciesAsync(watchlist.Id, boards, MaxVacancies, ct);
        var rendered = pages.Build(watchlist, vacancies, ctx.Language, fold: false);

        var head = new StringBuilder(
            $"<h6>{BotTexts.Get(
                TextKey.CompanyVacanciesTitle,
                ctx.Language,
                MessageFormatter.Escape(entry.CompanyName),
                MessageFormatter.Escape(watchlist.Name))}</h6>");

        if (rendered.Count == 0)
        {
            return new ScreenView(
                head.Append($"<p>{BotTexts.Get(TextKey.CompanyVacanciesEmpty, ctx.Language)}</p>").ToString(),
                Keyboard(ctx, entryId, watchlist.Id, 0, 0));
        }

        var current = Math.Clamp(page, 0, rendered.Count - 1);

        return new ScreenView(
            head.Append(rendered[current]).ToString(),
            Keyboard(ctx, entryId, watchlist.Id, current, rendered.Count));
    }

    private static InlineKeyboardMarkup Keyboard(
        BotContext ctx,
        long entryId,
        long watchlistId,
        int page,
        int totalPages) =>
        new KeyboardBuilder(ctx.Language)
            .Paging(CallbackAction.CompanyVacancies, entryId, page, totalPages)
            .Button(TextKey.CompanyVacanciesAll, CallbackAction.VacanciesOpen, watchlistId)
            .Build(CallbackAction.WatchlistOpen, watchlistId);
}
