using System.Text;
using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Core.Pipeline;
using JobsPulse.Sinks.Telegram.Infrastructure;
using JobsPulse.Sinks.Telegram.Infrastructure.Localization;
using JobsPulse.Sinks.Telegram.Models;

namespace JobsPulse.Sinks.Telegram.Pipeline.Screens;

/// <summary>
/// The two list screens: the user's own watchlists (editable) and everybody's (examples). Both name the owner
/// explicitly, so it is never a guess whose list is being looked at.
/// </summary>
public sealed class WatchlistsScreen(WatchService watch, IBotUserStorage users, IStateStore stateStore)
{
    public async Task<ScreenView> RenderMineAsync(BotContext ctx, int page, CancellationToken ct)
    {
        var mine = await watch.ListByOwnerAsync(ctx.UserId, ct);

        var sb = new StringBuilder($"<h6>{BotTexts.Get(TextKey.MyWatchlistsTitle, ctx.Language)}</h6>");

        if (mine.Count == 0)
        {
            sb.Append($"<p>{BotTexts.Get(TextKey.MyWatchlistsEmpty, ctx.Language)}</p>");

            var empty = new KeyboardBuilder(ctx.Language)
                .Button(TextKey.WatchlistCreate, CallbackAction.WatchlistNew)
                .Build(CallbackAction.Menu);

            return new ScreenView(sb.ToString(), empty);
        }

        var matches = await stateStore.CountMatchesByWatchlistAsync(ct);
        var pageItems = Paged(mine, page, out var totalPages);

        sb.Append("<p>");
        foreach (var watchlist in pageItems)
        {
            var state = watchlist.Enabled
                ? BotTexts.Get(TextKey.WatchlistStateActive, ctx.Language)
                : BotTexts.Get(TextKey.WatchlistStatePaused, ctx.Language);

            sb.Append($"• <b>{MessageFormatter.Escape(watchlist.Name)}</b> — {state}, "
                      + $"{BotTexts.Get(TextKey.WatchlistCompaniesLabel, ctx.Language).ToLowerInvariant()}: "
                      + $"<b>{watchlist.Entries.Count(e => e.Enabled)}</b>, "
                      + $"{BotTexts.Get(TextKey.WatchlistMatchesLabel, ctx.Language).ToLowerInvariant()}: "
                      + $"<b>{matches.GetValueOrDefault(watchlist.Id)}</b><br>");
        }

        sb.Append("</p>");

        var keyboard = new KeyboardBuilder(ctx.Language)
            .Items(pageItems, w => w.Name, CallbackAction.WatchlistOpen, w => w.Id)
            .Paging(CallbackAction.MyWatchlists, 0, page, totalPages)
            .Button(TextKey.WatchlistCreate, CallbackAction.WatchlistNew)
            .Build(CallbackAction.Menu);

        return new ScreenView(sb.ToString(), keyboard);
    }

    public async Task<ScreenView> RenderAllAsync(BotContext ctx, int page, CancellationToken ct)
    {
        var all = await watch.ListAsync(ct);

        // Own lists first - the ones the user can act on are the ones they are looking for.
        var ordered = all
            .OrderByDescending(w => WatchlistAccess.IsOwn(ctx, w))
            .ThenBy(w => w.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var owners = await users.GetManyAsync(
            [.. ordered.Where(w => w.OwnerUserId is not null).Select(w => w.OwnerUserId!.Value).Distinct()], ct);

        var pageItems = Paged(ordered, page, out var totalPages);

        var sb = new StringBuilder($"<h6>{BotTexts.Get(TextKey.AllWatchlistsTitle, ctx.Language)}</h6>");
        sb.Append($"<p>{BotTexts.Get(TextKey.AllWatchlistsHint, ctx.Language)}</p><p>");

        foreach (var watchlist in pageItems)
        {
            var owner = BotFormatter.OwnerLabel(ctx, watchlist, owners);
            var mark = WatchlistAccess.IsOwn(ctx, watchlist) ? "⭐ " : string.Empty;

            sb.Append($"• {mark}<b>{MessageFormatter.Escape(watchlist.Name)}</b> — "
                      + $"{MessageFormatter.Escape(owner)}, "
                      + $"{BotTexts.Get(TextKey.WatchlistCompaniesLabel, ctx.Language).ToLowerInvariant()}: "
                      + $"<b>{watchlist.Entries.Count(e => e.Enabled)}</b><br>");
        }

        sb.Append("</p>");

        var keyboard = new KeyboardBuilder(ctx.Language)
            .Items(
                pageItems,
                w => WatchlistAccess.IsOwn(ctx, w) ? $"⭐ {w.Name}" : w.Name,
                CallbackAction.WatchlistOpen,
                w => w.Id)
            .Paging(CallbackAction.AllWatchlists, 0, page, totalPages)
            .Build(CallbackAction.Menu);

        return new ScreenView(sb.ToString(), keyboard);
    }

    internal static List<T> Paged<T>(IReadOnlyList<T> items, int page, out int totalPages) =>
        Paged(items, page, KeyboardBuilder.PageSize, out totalPages);

    /// <summary>
    /// A list whose rows are text rather than buttons is not bound by the keyboard size - it takes its own page size.
    /// </summary>
    internal static List<T> Paged<T>(IReadOnlyList<T> items, int page, int pageSize, out int totalPages)
    {
        totalPages = Math.Max(1, (int)Math.Ceiling(items.Count / (double)pageSize));
        page = Math.Clamp(page, 0, totalPages - 1);

        return [.. items.Skip(page * pageSize).Take(pageSize)];
    }
}
