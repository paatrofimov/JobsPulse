using System.Text;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Core.Pipeline;
using JobsPulse.Sinks.Telegram.Infrastructure;
using JobsPulse.Sinks.Telegram.Infrastructure.Localization;
using JobsPulse.Sinks.Telegram.Models;

namespace JobsPulse.Sinks.Telegram.Pipeline.Screens;

/// <summary>
/// Every disabled company the user has, across all of their watchlists, on one screen. Without it a switched-off
/// company is effectively lost: it lives inside one watchlist page and nobody remembers where.
/// One tap puts it back to work.
/// </summary>
public sealed class DisabledCompaniesScreen(WatchService watch, WatchlistAccess access)
{
    public async Task<ScreenView> RenderAsync(BotContext ctx, int page, CancellationToken ct)
    {
        var mine = await watch.ListByOwnerAsync(ctx.UserId, ct);

        var disabled = mine
            .SelectMany(w => w.Entries.Where(e => !e.Enabled).Select(e => (Watchlist: w, Entry: e)))
            .OrderBy(x => x.Watchlist.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Entry.CompanyName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var sb = new StringBuilder($"<h6>{BotTexts.Get(TextKey.DisabledTitle, ctx.Language)}</h6>");

        if (disabled.Count == 0)
        {
            return new ScreenView(
                sb.Append($"<p>{BotTexts.Get(TextKey.DisabledEmpty, ctx.Language)}</p>").ToString(),
                new KeyboardBuilder(ctx.Language).Build(CallbackAction.Menu));
        }

        var pageItems = WatchlistsScreen.Paged(disabled, page, out var totalPages);

        sb.Append($"<p>{BotTexts.Get(TextKey.DisabledHint, ctx.Language)}</p><p>");

        foreach (var (watchlist, entry) in pageItems)
        {
            var discovered = entry.Origin == BoardOrigin.Discovery
                ? $" · {BotTexts.Get(TextKey.CompanyFoundByDiscovery, ctx.Language)}"
                : string.Empty;

            sb.Append($"{BotFormatter.DisabledGlyph} <b>{MessageFormatter.Escape(entry.CompanyName)}</b> — "
                      + $"{MessageFormatter.Escape(watchlist.Name)}{discovered}<br>");
        }

        sb.Append("</p>");

        var keyboard = new KeyboardBuilder(ctx.Language)
            .Items(
                pageItems,
                x => $"{BotFormatter.ActiveGlyph} {x.Entry.CompanyName}",
                CallbackAction.CompanyRestore,
                x => x.Entry.Id,
                page)
            .Paging(CallbackAction.DisabledCompanies, 0, page, totalPages)
            .Build(CallbackAction.Menu);

        return new ScreenView(sb.ToString(), keyboard);
    }

    public async Task<ScreenView> RestoreAsync(BotContext ctx, long entryId, int page, CancellationToken ct)
    {
        var resolved = await access.ResolveEntryAsync(ctx, entryId, ct);
        if (resolved is not { Entry: { } entry, Watchlist: { } watchlist })
            return await RenderAsync(ctx, page, ct);

        if (!resolved.CanEdit)
            return (await RenderAsync(ctx, page, ct)).WithToast(BotTexts.Get(TextKey.NotAllowed, ctx.Language));

        await watch.SetEntryEnabledAsync(watchlist, entry.Id.ToString(), true, ct);

        var view = await RenderAsync(ctx, page, ct);

        return view.WithToast(BotTexts.Get(TextKey.CompanyEnabled, ctx.Language, entry.CompanyName));
    }
}
