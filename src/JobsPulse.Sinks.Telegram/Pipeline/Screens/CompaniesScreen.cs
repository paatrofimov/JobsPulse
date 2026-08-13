using System.Text;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Core.Pipeline;
using JobsPulse.Sinks.Telegram.Infrastructure;
using JobsPulse.Sinks.Telegram.Infrastructure.Localization;
using JobsPulse.Sinks.Telegram.Models;

namespace JobsPulse.Sinks.Telegram.Pipeline.Screens;

/// <summary>
/// Companies of one watchlist, grouped by the source they are watched through. The list itself answers the three
/// questions a user has - which are being watched, which are switched off and which have already been worked through -
/// with a glyph per row and a legend, so nothing has to be opened to find out.
///
/// The rows are text, not buttons: a button per company capped the page at eight and filled the screen with labels
/// that only repeat the list. One «change a company» button asks for a name instead, so a page carries
/// <see cref="PageSize"/> companies.
/// </summary>
public sealed class CompaniesScreen(WatchService watch, WatchlistAccess access, UserSessionStore sessions)
{
    /// <summary>
    /// Companies per page. Bound by the telegram message limit rather than by the keyboard: ~30 rows of «glyph, name,
    /// status» stay well inside 4096 characters.
    /// </summary>
    private const int PageSize = 30;

    public async Task<ScreenView> RenderAsync(BotContext ctx, long watchlistId, int page, CancellationToken ct)
    {
        var resolved = await access.ResolveAsync(ctx, watchlistId, ct);
        if (resolved.Watchlist is not { } watchlist)
            return Gone(ctx);

        var sb = new StringBuilder(
            $"<h6>{BotTexts.Get(TextKey.CompaniesTitle, ctx.Language, MessageFormatter.Escape(watchlist.Name))}</h6>");

        if (watchlist.Entries.Count == 0)
        {
            sb.Append($"<p>{BotTexts.Get(TextKey.CompaniesEmpty, ctx.Language)}</p>");

            var empty = new KeyboardBuilder(ctx.Language)
                .ButtonIf(resolved.CanEdit, TextKey.WatchlistAddCompany, CallbackAction.CompanyAdd, watchlistId)
                .Build(CallbackAction.WatchlistOpen, watchlistId);

            return new ScreenView(sb.ToString(), empty);
        }

        var ordered = CompanyList.Order(watchlist.Entries);
        var pageItems = WatchlistsScreen.Paged(ordered, page, PageSize, out var totalPages);

        sb.Append($"<p>{BotTexts.Get(TextKey.CompanyLegend, ctx.Language)}</p>");

        foreach (var group in CompanyList.GroupBySource(pageItems))
        {
            sb.Append($"<p><b>{MessageFormatter.Escape(group.SourceId)}</b> · {group.Entries.Count}<br>");

            foreach (var entry in group.Entries)
                AppendRow(sb, entry, ctx);

            sb.Append("</p>");
        }

        var keyboard = new KeyboardBuilder(ctx.Language)
            .Paging(CallbackAction.CompaniesOpen, watchlistId, page, totalPages)
            .ButtonIf(resolved.CanEdit, TextKey.CompanyChange, CallbackAction.CompanyFind, watchlistId, page)
            .ButtonIf(resolved.CanEdit, TextKey.WatchlistAddCompany, CallbackAction.CompanyAdd, watchlistId)
            .Build(CallbackAction.WatchlistOpen, watchlistId);

        return new ScreenView(sb.ToString(), keyboard);
    }

    private static void AppendRow(StringBuilder sb, WatchlistEntry entry, BotContext ctx)
    {
        sb.Append($"{BotFormatter.EntryGlyph(entry)} <b>{MessageFormatter.Escape(entry.CompanyName)}</b> — "
                  + $"{BotFormatter.EntryStatus(entry, ctx.Language)}");

        if (entry.WorkedAt is { } workedAt)
        {
            sb.Append(", ")
                .Append(BotTexts.Get(
                    TextKey.CompanyWorkedOn, ctx.Language, BotTexts.FormatDate(workedAt, false, ctx.Language)));
        }

        if (entry.Origin == BoardOrigin.Discovery)
            sb.Append($" · {BotTexts.Get(TextKey.CompanyFoundByDiscovery, ctx.Language)}");

        sb.Append("<br>");
    }

    /// <summary>Arms the «which company» question - the one button that replaced a button per row.</summary>
    public async Task<ScreenView> PromptFindAsync(BotContext ctx, long watchlistId, int page, CancellationToken ct)
    {
        var resolved = await access.ResolveAsync(ctx, watchlistId, ct);
        if (resolved.Watchlist is null)
            return Gone(ctx);

        if (!resolved.CanEdit)
        {
            return (await RenderAsync(ctx, watchlistId, page, ct))
                .WithToast(BotTexts.Get(TextKey.NotAllowed, ctx.Language));
        }

        sessions.Await(ctx.UserId, PendingInputKind.CompanyName, watchlistId);

        return new ScreenView(
            $"<p>{BotTexts.Get(TextKey.CompanyFindPrompt, ctx.Language)}</p>",
            new KeyboardBuilder(ctx.Language).Build(CallbackAction.CompaniesOpen, watchlistId, page));
    }

    /// <summary>
    /// The answer to that question: one match opens the company, several become buttons, none re-arms the step -
    /// a miss is usually a typo, and retyping beats walking the menu again.
    /// </summary>
    public async Task<ScreenView> FindAsync(BotContext ctx, long watchlistId, string query, CancellationToken ct)
    {
        var resolved = await access.ResolveAsync(ctx, watchlistId, ct);
        if (resolved.Watchlist is not { } watchlist)
            return Gone(ctx);

        if (!resolved.CanEdit)
        {
            return (await RenderAsync(ctx, watchlistId, 0, ct))
                .WithToast(BotTexts.Get(TextKey.NotAllowed, ctx.Language));
        }

        var matches = CompanyList.Find(watchlist.Entries, query);

        if (matches.Count == 1)
            return await RenderCompanyAsync(ctx, matches[0].Id, 0, ct);

        if (matches.Count == 0)
        {
            sessions.Await(ctx.UserId, PendingInputKind.CompanyName, watchlistId);

            return new ScreenView(
                $"<p>{BotTexts.Get(TextKey.CompanyFindNotFound, ctx.Language, MessageFormatter.Escape(query))}</p>",
                new KeyboardBuilder(ctx.Language).Build(CallbackAction.CompaniesOpen, watchlistId));
        }

        var keyboard = new KeyboardBuilder(ctx.Language)
            .Items(
                matches.Take(KeyboardBuilder.PageSize),
                BotFormatter.EntryButton,
                CallbackAction.CompanyOpen,
                e => e.Id)
            .Build(CallbackAction.CompaniesOpen, watchlistId);

        return new ScreenView(
            $"<p>{BotTexts.Get(TextKey.CompanyFindMany, ctx.Language, MessageFormatter.Escape(query))}</p>",
            keyboard);
    }

    /// <summary>One company and what can be done to it. The page is kept so «back» returns to the same page.</summary>
    public async Task<ScreenView> RenderCompanyAsync(BotContext ctx, long entryId, int page, CancellationToken ct)
    {
        var resolved = await access.ResolveEntryAsync(ctx, entryId, ct);
        if (resolved is not { Entry: { } entry, Watchlist: { } watchlist })
            return Gone(ctx);

        var sb = new StringBuilder($"<h6>{MessageFormatter.Escape(entry.CompanyName)}</h6>");

        sb.Append($"<p>{MessageFormatter.Escape(watchlist.Name)}<br>"
                  + $"{BotFormatter.EntryGlyph(entry)} {BotFormatter.EntryStatus(entry, ctx.Language)}");

        if (entry.WorkedAt is { } workedAt)
        {
            sb.Append("<br>")
                .Append(BotTexts.Get(
                    TextKey.CompanyWorkedOn, ctx.Language, BotTexts.FormatDate(workedAt, false, ctx.Language)));
        }

        if (entry.Origin == BoardOrigin.Discovery)
            sb.Append($"<br>{BotTexts.Get(TextKey.CompanyFoundByDiscovery, ctx.Language)}");

        sb.Append("</p>");

        if (!resolved.CanEdit)
        {
            return new ScreenView(
                sb.Append($"<p>{BotTexts.Get(TextKey.WatchlistReadOnly, ctx.Language)}</p>").ToString(),
                new KeyboardBuilder(ctx.Language).Build(CallbackAction.CompaniesOpen, watchlist.Id, page));
        }

        var keyboard = new KeyboardBuilder(ctx.Language)
            .Button(
                entry.IsWorked ? TextKey.CompanyUnmarkWorked : TextKey.CompanyMarkWorked,
                CallbackAction.CompanyToggleWorked,
                entry.Id,
                page)
            .Button(
                entry.Enabled ? TextKey.CompanyDisable : TextKey.CompanyEnable,
                CallbackAction.CompanyToggleEnabled,
                entry.Id,
                page)
            .Button(TextKey.CompanyRemove, CallbackAction.CompanyRemove, entry.Id, page)
            .Build(CallbackAction.CompaniesOpen, watchlist.Id, page);

        return new ScreenView(sb.ToString(), keyboard);
    }

    public async Task<ScreenView> ToggleWorkedAsync(BotContext ctx, long entryId, int page, CancellationToken ct)
    {
        var resolved = await access.ResolveEntryAsync(ctx, entryId, ct);
        if (resolved is not { Entry: { } entry })
            return Gone(ctx);

        if (!resolved.CanEdit)
            return await Denied(ctx, entryId, page, ct);

        await watch.SetEntryWorkedAsync(entry.Id, !entry.IsWorked, ct);

        var view = await RenderCompanyAsync(ctx, entryId, page, ct);

        return view.WithToast(BotTexts.Get(
            entry.IsWorked ? TextKey.CompanyUnmarkedWorked : TextKey.CompanyMarkedWorked,
            ctx.Language,
            entry.CompanyName));
    }

    public async Task<ScreenView> ToggleEnabledAsync(BotContext ctx, long entryId, int page, CancellationToken ct)
    {
        var resolved = await access.ResolveEntryAsync(ctx, entryId, ct);
        if (resolved is not { Entry: { } entry, Watchlist: { } watchlist })
            return Gone(ctx);

        if (!resolved.CanEdit)
            return await Denied(ctx, entryId, page, ct);

        await watch.SetEntryEnabledAsync(watchlist, entry.Id.ToString(), !entry.Enabled, ct);

        var view = await RenderCompanyAsync(ctx, entryId, page, ct);

        return view.WithToast(BotTexts.Get(
            entry.Enabled ? TextKey.CompanyDisabled : TextKey.CompanyEnabled, ctx.Language, entry.CompanyName));
    }

    public async Task<ScreenView> RemoveAsync(BotContext ctx, long entryId, int page, CancellationToken ct)
    {
        var resolved = await access.ResolveEntryAsync(ctx, entryId, ct);
        if (resolved is not { Entry: { } entry, Watchlist: { } watchlist })
            return Gone(ctx);

        if (!resolved.CanEdit)
            return await Denied(ctx, entryId, page, ct);

        var result = await watch.RemoveEntryAsync(watchlist, entry.Id.ToString(), ct);

        var toast = result switch
        {
            // A discovered company is disabled rather than deleted, or the next sweep would bring it back.
            EntryRemoveResult.Disabled =>
                BotTexts.Get(TextKey.CompanyDisabledInsteadOfRemoved, ctx.Language, entry.CompanyName),
            EntryRemoveResult.Removed => BotTexts.Get(TextKey.CompanyRemoved, ctx.Language, entry.CompanyName),
            _ => BotTexts.Get(TextKey.SomethingWentWrong, ctx.Language)
        };

        var view = await RenderAsync(ctx, watchlist.Id, page, ct);

        return view.WithToast(toast);
    }

    private async Task<ScreenView> Denied(BotContext ctx, long entryId, int page, CancellationToken ct)
    {
        var view = await RenderCompanyAsync(ctx, entryId, page, ct);

        return view.WithToast(BotTexts.Get(TextKey.NotAllowed, ctx.Language));
    }

    private static ScreenView Gone(BotContext ctx)
    {
        var keyboard = new KeyboardBuilder(ctx.Language).Build(CallbackAction.MyWatchlists);

        return new ScreenView($"<p>{BotTexts.Get(TextKey.WatchlistGone, ctx.Language)}</p>", keyboard);
    }
}
