using System.Text;
using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model.Domain;
using JobsPulse.Core.Pipeline;
using JobsPulse.Sinks.Telegram.Infrastructure;
using JobsPulse.Sinks.Telegram.Infrastructure.Localization;
using JobsPulse.Sinks.Telegram.Models;

namespace JobsPulse.Sinks.Telegram.Pipeline.Screens;

/// <summary>
/// The found vacancies, opened by watchlist name: the user picks a list and pages through what matched it. This is
/// the browsable counterpart of the push notifications - the same matches, but on demand and without scrolling the
/// chat history.
/// </summary>
public sealed class VacanciesScreen(WatchService watch, WatchlistAccess access, IStateStore stateStore)
{
    private const int VacanciesPerPage = 10;

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
        var pageItems = WatchlistsScreen.Paged(mine, page, out var totalPages);

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

    public async Task<ScreenView> RenderAsync(BotContext ctx, long watchlistId, int page, CancellationToken ct)
    {
        var resolved = await access.ResolveAsync(ctx, watchlistId, ct);
        if (resolved.Watchlist is not { } watchlist)
        {
            return new ScreenView(
                $"<p>{BotTexts.Get(TextKey.WatchlistGone, ctx.Language)}</p>",
                new KeyboardBuilder(ctx.Language).Build(CallbackAction.VacanciesPick));
        }

        var total = (await stateStore.CountMatchesByWatchlistAsync(ct)).GetValueOrDefault(watchlistId);

        var vacancies = await stateStore.LoadMatchedVacanciesAsync(
            watchlistId, VacanciesPerPage, page * VacanciesPerPage, ct);

        var sb = new StringBuilder(
            $"<h6>{BotTexts.Get(TextKey.VacanciesTitle, ctx.Language, MessageFormatter.Escape(watchlist.Name))}</h6>");

        if (vacancies.Count == 0)
        {
            return new ScreenView(
                sb.Append($"<p>{BotTexts.Get(TextKey.VacanciesEmpty, ctx.Language)}</p>").ToString(),
                new KeyboardBuilder(ctx.Language).Build(CallbackAction.VacanciesPick));
        }

        sb.Append($"<p>{BotTexts.Get(TextKey.VacanciesCount, ctx.Language, total)}</p>");

        foreach (var vacancy in vacancies)
            sb.Append("<p>").Append(Render(vacancy, ctx)).Append("</p>");

        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)VacanciesPerPage));

        var keyboard = new KeyboardBuilder(ctx.Language)
            .Paging(CallbackAction.VacanciesOpen, watchlistId, page, totalPages)
            .Build(CallbackAction.VacanciesPick);

        return new ScreenView(sb.ToString(), keyboard);
    }

    private static string Render(Vacancy vacancy, BotContext ctx)
    {
        var title = $"<a href=\"{MessageFormatter.Escape(vacancy.Url)}\">"
                    + $"<b>{MessageFormatter.Escape(vacancy.Title)}</b></a>";

        var location = vacancy.Location
                       ?? (vacancy.Offices.Count > 0 ? string.Join(" · ", vacancy.Offices) : null)
                       ?? BotTexts.Get(TextKey.VacancyUnknownLocation, ctx.Language);

        var published = vacancy.FirstPublishedAt ?? vacancy.UpdatedAt ?? vacancy.FirstSeenAt;

        var date = published is { } value
            ? BotTexts.FormatDate(value, false, ctx.Language)
            : BotTexts.Get(TextKey.Nothing, ctx.Language);

        return $"{title}<br> {MessageFormatter.Escape(location)} · {date}";
    }
}
