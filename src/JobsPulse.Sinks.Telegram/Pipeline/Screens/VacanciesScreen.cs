using System.Text;
using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Core.Options;
using JobsPulse.Core.Pipeline;
using JobsPulse.Sinks.Telegram.Infrastructure;
using JobsPulse.Sinks.Telegram.Infrastructure.Localization;
using JobsPulse.Sinks.Telegram.Models;
using Microsoft.Extensions.Options;
using Telegram.Bot.Types.ReplyMarkups;

namespace JobsPulse.Sinks.Telegram.Pipeline.Screens;

/// <summary>
/// The found vacancies, opened by watchlist name: the user picks a list and reads what matched it, grouped by company
/// the same way the notifications are - or by location, by month, or by company ordered by how hot the company is.
/// This is the browsable counterpart of the push notifications - the same matches, but on demand and without
/// scrolling the chat history. Vacancies of disabled companies are left out: the list shows what is being watched.
/// </summary>
public sealed class VacanciesScreen(
    WatchService watch,
    WatchlistAccess access,
    IStateStore stateStore,
    VacancyPageBuilder pages,
    IOptionsMonitor<DeliveryOptions> deliveryOptions,
    TimeProvider clock)
{
    /// <summary>
    /// How much of a watchlist feed one screen session may hold. Everything below the cap is rendered - grouped and
    /// packed into as few pages as telegram's message limit allows - so a normal watchlist is fully browsable, and with
    /// every block folded away it stays one screen.
    /// </summary>
    private const int MaxVacancies = 500;

    public async Task<ScreenView> RenderPickAsync(BotContext ctx, int page, CancellationToken ct)
    {
        var mine = await watch.ListByOwnerAsync(ctx.UserId, ct);

        if (mine.Count == 0)
        {
            return new ScreenView(
                $"<h6>{BotTexts.Get(TextKey.MenuVacancies, ctx.Language)}</h6>"
                + $"<p>{BotTexts.Get(TextKey.MyWatchlistsEmpty, ctx.Language)}</p>",
                new KeyboardBuilder(ctx.Language)
                    .Button(TextKey.WatchlistCreate, CallbackAction.WatchlistNew)
                    .Build(CallbackAction.Menu));
        }

        var matches = await stateStore.CountMatchesByWatchlistAsync(ct);
        var pageItems = Pager.Slice(mine, ref page, out var totalPages);

        var keyboard = new KeyboardBuilder(ctx.Language)
            .Items(
                pageItems,
                w => $"{w.Name} — {matches.GetValueOrDefault(w.Id)}",
                CallbackAction.VacanciesOpen,
                w => w.Id)
            .Paging(CallbackAction.VacanciesPick, 0, page, totalPages)
            .Build(CallbackAction.Menu);

        return new ScreenView(
            $"<h6>{BotTexts.Get(TextKey.MenuVacancies, ctx.Language)}</h6>"
            + $"<p>{BotTexts.Get(TextKey.VacanciesPickWatchlist, ctx.Language)}</p>",
            keyboard);
    }

    public async Task<ScreenView> RenderAsync(
        BotContext ctx,
        long watchlistId,
        int page,
        CancellationToken ct,
        VacancyGrouping grouping = VacancyGrouping.Company)
    {
        var resolved = await access.ResolveAsync(ctx, watchlistId, ct);
        if (resolved.Watchlist is not { } watchlist)
        {
            return new ScreenView(
                $"<p>{BotTexts.Get(TextKey.WatchlistGone, ctx.Language)}</p>",
                new KeyboardBuilder(ctx.Language).Build(CallbackAction.VacanciesPick));
        }

        // A company the user has switched off is not watched any more, so its vacancies are not part of the feed.
        var vacancies = VacancyPageBuilder.OfActiveCompanies(
            watchlist,
            await stateStore.LoadMatchedVacanciesAsync(watchlistId, MaxVacancies, ct));

        var head = new StringBuilder(
            $"<h6>{BotTexts.Get(TextKey.VacanciesTitle, ctx.Language, MessageFormatter.Escape(watchlist.Name))}</h6>");

        // The indicator is one query and only the ordering of one grouping depends on it.
        var activity = grouping == VacancyGrouping.Activity
            ? await stateStore.CountBoardActivityAsync(ActivitySince(), ct)
            : null;

        var rendered = pages.Build(watchlist, vacancies, ctx.Language, grouping, activity);

        if (rendered.Count == 0)
        {
            return new ScreenView(
                head.Append($"<p>{BotTexts.Get(TextKey.VacanciesEmpty, ctx.Language)}</p>").ToString(),
                Keyboard(ctx, watchlistId, 0, 0, grouping));
        }

        // A stale button from a previous, longer feed must not land on nothing.
        var current = Math.Clamp(page, 0, rendered.Count - 1);

        var total = (await stateStore.CountMatchesByWatchlistAsync(ct)).GetValueOrDefault(watchlistId);

        head.Append(
            vacancies.Count < total
                ? $"<p>{BotTexts.Get(TextKey.VacanciesShownOf, ctx.Language, vacancies.Count, total)}</p>"
                : $"<p>{BotTexts.Get(TextKey.VacanciesCount, ctx.Language, vacancies.Count)}</p>");

        if (grouping == VacancyGrouping.Activity)
            head.Append($"<p>{BotTexts.Get(TextKey.ActivityLegend, ctx.Language)}</p>");

        return new ScreenView(
            head.Append(rendered[current]).ToString(),
            Keyboard(ctx, watchlistId, current, rendered.Count, grouping));
    }

    private DateTimeOffset ActivitySince() =>
        clock.GetUtcNow().AddDays(-deliveryOptions.CurrentValue.ActivityWindowDays);

    /// <summary>
    /// Paging stays inside the grouping the reader chose - the action is what carries it - and the three groupings
    /// they are not looking at share one row, so switching is one tap from anywhere.
    /// </summary>
    private static InlineKeyboardMarkup Keyboard(
        BotContext ctx,
        long watchlistId,
        int page,
        int totalPages,
        VacancyGrouping grouping)
    {
        var modes = new (VacancyGrouping Grouping, TextKey Label, CallbackAction Action)[]
        {
            (VacancyGrouping.Company, TextKey.VacanciesByCompany, CallbackAction.VacanciesOpen),
            (VacancyGrouping.Location, TextKey.VacanciesByLocation, CallbackAction.VacanciesByLocation),
            (VacancyGrouping.Month, TextKey.VacanciesByMonth, CallbackAction.VacanciesByMonth),
            (VacancyGrouping.Activity, TextKey.VacanciesByActivity, CallbackAction.VacanciesByActivity)
        };

        return new KeyboardBuilder(ctx.Language)
            .Paging(modes.First(m => m.Grouping == grouping).Action, watchlistId, page, totalPages)
            .Modes(
                [.. modes.Where(m => m.Grouping != grouping).Select(m => (m.Label, m.Action))],
                watchlistId)
            .Build(CallbackAction.VacanciesPick);
    }
}
