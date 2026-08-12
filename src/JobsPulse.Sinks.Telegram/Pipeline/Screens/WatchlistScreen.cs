using System.Text;
using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Core.Pipeline;
using JobsPulse.Sinks.Telegram.Infrastructure;
using JobsPulse.Sinks.Telegram.Infrastructure.Localization;
using JobsPulse.Sinks.Telegram.Models;

namespace JobsPulse.Sinks.Telegram.Pipeline.Screens;

/// <summary>
/// One watchlist: what it is watching and what can be done with it. A read-only one (somebody else's) shows the same
/// facts without the editing buttons - looking at an example must not offer actions that will be refused.
/// </summary>
public sealed class WatchlistScreen(
    WatchService watch,
    WatchlistAccess access,
    IBotUserStorage users,
    IStateStore stateStore,
    UserSessionStore sessions)
{
    private const int MaxNameLength = 60;

    public async Task<ScreenView> RenderAsync(BotContext ctx, long watchlistId, CancellationToken ct)
    {
        var resolved = await access.ResolveAsync(ctx, watchlistId, ct);
        if (resolved.Watchlist is not { } watchlist)
            return Gone(ctx);

        var owners = watchlist.OwnerUserId is { } ownerId
            ? await users.GetManyAsync([ownerId], ct)
            : new Dictionary<long, BotUser>();

        var matches = await stateStore.CountMatchesByWatchlistAsync(ct);

        var state = watchlist.Enabled
            ? BotTexts.Get(TextKey.WatchlistStateActive, ctx.Language)
            : BotTexts.Get(TextKey.WatchlistStatePaused, ctx.Language);

        var sb = new StringBuilder(
            $"<h6>{MessageFormatter.Escape(watchlist.Name)}</h6>");

        sb.Append($"<p>{MessageFormatter.Escape(BotFormatter.OwnerLabel(ctx, watchlist, owners))} · {state}<br>");
        sb.Append($"{BotTexts.Get(TextKey.WatchlistCompaniesLabel, ctx.Language)}: "
                  + $"<b>{watchlist.Entries.Count(e => e.Enabled)}</b> / {watchlist.Entries.Count}<br>");
        sb.Append($"{BotTexts.Get(TextKey.WatchlistMatchesLabel, ctx.Language)}: "
                  + $"<b>{matches.GetValueOrDefault(watchlist.Id)}</b></p>");

        sb.Append($"<p>{BotFormatter.Filter(watchlist.Filter, ctx.Language)}</p>");

        if (!resolved.CanEdit)
            sb.Append($"<p>{BotTexts.Get(TextKey.WatchlistReadOnly, ctx.Language)}</p>");

        var keyboard = new KeyboardBuilder(ctx.Language)
            .Button(TextKey.WatchlistOpenVacancies, CallbackAction.VacanciesOpen, watchlist.Id)
            .Button(TextKey.WatchlistOpenCompanies, CallbackAction.CompaniesOpen, watchlist.Id)
            .ButtonIf(resolved.CanEdit, TextKey.WatchlistAddCompany, CallbackAction.CompanyAdd, watchlist.Id)
            .ButtonIf(resolved.CanEdit, TextKey.WatchlistEditFilter, CallbackAction.FilterOpen, watchlist.Id)
            .ButtonIf(resolved.CanEdit, TextKey.WatchlistRename, CallbackAction.WatchlistRename, watchlist.Id)
            .ButtonIf(
                resolved.CanEdit,
                watchlist.Enabled ? TextKey.WatchlistPause : TextKey.WatchlistResume,
                CallbackAction.WatchlistTogglePaused,
                watchlist.Id)
            .ButtonIf(resolved.CanEdit, TextKey.WatchlistDelete, CallbackAction.WatchlistDeleteAsk, watchlist.Id)
            .Build(CallbackAction.MyWatchlists);

        return new ScreenView(sb.ToString(), keyboard);
    }

    public ScreenView PromptCreate(BotContext ctx)
    {
        sessions.Await(ctx.UserId, PendingInputKind.WatchlistName, 0);

        var keyboard = new KeyboardBuilder(ctx.Language).Build(CallbackAction.MyWatchlists);

        return new ScreenView($"<p>{BotTexts.Get(TextKey.WatchlistCreatePrompt, ctx.Language)}</p>", keyboard);
    }

    public async Task<ScreenView> CreateAsync(BotContext ctx, string name, CancellationToken ct)
    {
        if (TooLong(name))
            return Retry(ctx, TextKey.WatchlistNameTooLong, PendingInputKind.WatchlistName, 0, MaxNameLength);

        var created = await watch.CreateAsync(name, ctx.UserId, ct);
        if (created is null)
        {
            return Retry(
                ctx, TextKey.WatchlistNameTaken, PendingInputKind.WatchlistName, 0, MessageFormatter.Escape(name));
        }

        var view = await RenderAsync(ctx, created.Id, ct);

        return view with
        {
            Html = $"<p>{BotTexts.Get(TextKey.WatchlistCreated, ctx.Language, MessageFormatter.Escape(created.Name))}"
                   + $"</p>{view.Html}"
        };
    }

    public async Task<ScreenView> PromptRenameAsync(BotContext ctx, long watchlistId, CancellationToken ct)
    {
        var resolved = await access.ResolveAsync(ctx, watchlistId, ct);
        if (resolved.Watchlist is not { } watchlist)
            return Gone(ctx);

        if (!resolved.CanEdit)
            return await Denied(ctx, watchlistId, ct);

        sessions.Await(ctx.UserId, PendingInputKind.WatchlistRename, watchlistId);

        var keyboard = new KeyboardBuilder(ctx.Language).Build(CallbackAction.WatchlistOpen, watchlistId);

        return new ScreenView(
            $"<p>{BotTexts.Get(TextKey.WatchlistRenamePrompt, ctx.Language, MessageFormatter.Escape(watchlist.Name))}"
            + "</p>",
            keyboard);
    }

    public async Task<ScreenView> RenameAsync(BotContext ctx, long watchlistId, string name, CancellationToken ct)
    {
        var resolved = await access.ResolveAsync(ctx, watchlistId, ct);
        if (resolved.Watchlist is not { } watchlist)
            return Gone(ctx);

        if (!resolved.CanEdit)
            return await Denied(ctx, watchlistId, ct);

        if (TooLong(name))
        {
            return Retry(
                ctx, TextKey.WatchlistNameTooLong, PendingInputKind.WatchlistRename, watchlistId, MaxNameLength);
        }

        if (!await watch.RenameAsync(watchlist, name, ct))
        {
            return Retry(
                ctx,
                TextKey.WatchlistNameTaken,
                PendingInputKind.WatchlistRename,
                watchlistId,
                MessageFormatter.Escape(name));
        }

        var view = await RenderAsync(ctx, watchlistId, ct);

        return view.WithToast(BotTexts.Get(TextKey.WatchlistRenamed, ctx.Language, name.Trim()));
    }

    public async Task<ScreenView> TogglePausedAsync(BotContext ctx, long watchlistId, CancellationToken ct)
    {
        var resolved = await access.ResolveAsync(ctx, watchlistId, ct);
        if (resolved.Watchlist is not { } watchlist)
            return Gone(ctx);

        if (!resolved.CanEdit)
            return await Denied(ctx, watchlistId, ct);

        await watch.SetEnabledAsync(watchlist, !watchlist.Enabled, ct);

        return await RenderAsync(ctx, watchlistId, ct);
    }

    public async Task<ScreenView> ConfirmDeleteAsync(BotContext ctx, long watchlistId, CancellationToken ct)
    {
        var resolved = await access.ResolveAsync(ctx, watchlistId, ct);
        if (resolved.Watchlist is not { } watchlist)
            return Gone(ctx);

        if (!resolved.CanEdit)
            return await Denied(ctx, watchlistId, ct);

        var keyboard = new KeyboardBuilder(ctx.Language)
            .Row(
                KeyboardBuilder.Make(
                    BotTexts.Get(TextKey.ConfirmYes, ctx.Language), CallbackAction.WatchlistDelete, watchlistId),
                KeyboardBuilder.Make(
                    BotTexts.Get(TextKey.ConfirmNo, ctx.Language), CallbackAction.WatchlistOpen, watchlistId))
            .Build(CallbackAction.WatchlistOpen, watchlistId);

        return new ScreenView(
            $"<p>{BotTexts.Get(TextKey.WatchlistDeleteConfirm, ctx.Language, MessageFormatter.Escape(watchlist.Name))}"
            + "</p>",
            keyboard);
    }

    public async Task<ScreenView> DeleteAsync(
        BotContext ctx,
        long watchlistId,
        WatchlistsScreen watchlists,
        CancellationToken ct)
    {
        var resolved = await access.ResolveAsync(ctx, watchlistId, ct);
        if (resolved.Watchlist is not { } watchlist)
            return Gone(ctx);

        if (!resolved.CanEdit)
            return await Denied(ctx, watchlistId, ct);

        await watch.RemoveAsync(watchlist, ct);

        var view = await watchlists.RenderMineAsync(ctx, 0, ct);

        return view.WithToast(BotTexts.Get(TextKey.WatchlistDeleted, ctx.Language, watchlist.Name));
    }

    /// <summary>A refused edit still lands on a usable screen - the watchlist itself, read-only.</summary>
    private async Task<ScreenView> Denied(BotContext ctx, long watchlistId, CancellationToken ct)
    {
        var view = await RenderAsync(ctx, watchlistId, ct);

        return view.WithToast(BotTexts.Get(TextKey.NotAllowed, ctx.Language));
    }

    private ScreenView Retry(
        BotContext ctx,
        TextKey reason,
        PendingInputKind kind,
        long watchlistId,
        params object?[] args)
    {
        // The step stays armed, so the user just types another answer instead of walking the menu again.
        sessions.Await(ctx.UserId, kind, watchlistId);

        var back = watchlistId == 0 ? CallbackAction.MyWatchlists : CallbackAction.WatchlistOpen;
        var keyboard = new KeyboardBuilder(ctx.Language).Build(back, watchlistId);

        return new ScreenView($"<p>{BotTexts.Get(reason, ctx.Language, args)}</p>", keyboard);
    }

    private static bool TooLong(string name) => name.Trim().Length > MaxNameLength;

    private static ScreenView Gone(BotContext ctx)
    {
        var keyboard = new KeyboardBuilder(ctx.Language).Build(CallbackAction.MyWatchlists);

        return new ScreenView($"<p>{BotTexts.Get(TextKey.WatchlistGone, ctx.Language)}</p>", keyboard);
    }
}
