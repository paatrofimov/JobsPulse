using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Core.Pipeline;
using JobsPulse.Sinks.Telegram.Infrastructure;
using JobsPulse.Sinks.Telegram.Infrastructure.Localization;
using JobsPulse.Sinks.Telegram.Models;

namespace JobsPulse.Sinks.Telegram.Pipeline.Screens;

/// <summary>
/// The filter, edited one rule at a time in plain words. A user never sees or types `FilterSpec` json - that stays an
/// admin tool; here every rule is a button and a comma separated answer.
/// </summary>
public sealed class FilterScreen(WatchService watch, WatchlistAccess access, UserSessionStore sessions)
{
    /// <summary>Offered freshness windows. 0 means «no limit» and clears the rule.</summary>
    private static readonly int[] FreshnessDays = [7, 14, 30, 60, 90];

    public async Task<ScreenView> RenderAsync(BotContext ctx, long watchlistId, CancellationToken ct)
    {
        var resolved = await access.ResolveAsync(ctx, watchlistId, ct);
        if (resolved.Watchlist is not { } watchlist)
            return Gone(ctx);

        var html = $"<h6>{BotTexts.Get(TextKey.FilterTitle, ctx.Language, MessageFormatter.Escape(watchlist.Name))}"
                   + $"</h6><p>{BotFormatter.Filter(watchlist.Filter, ctx.Language)}</p>";

        if (!resolved.CanEdit)
        {
            return new ScreenView(
                html + $"<p>{BotTexts.Get(TextKey.WatchlistReadOnly, ctx.Language)}</p>",
                new KeyboardBuilder(ctx.Language).Build(CallbackAction.WatchlistOpen, watchlistId));
        }

        var keyboard = new KeyboardBuilder(ctx.Language)
            .Button(TextKey.FilterKeywords, CallbackAction.FilterKeywords, watchlistId)
            .Button(TextKey.FilterExcluded, CallbackAction.FilterExcluded, watchlistId)
            .Button(TextKey.FilterLocations, CallbackAction.FilterLocations, watchlistId)
            .Button(TextKey.FilterFreshness, CallbackAction.FilterFreshnessAsk, watchlistId)
            .ButtonIf(!watchlist.Filter.IsEmpty, TextKey.FilterClear, CallbackAction.FilterClear, watchlistId)
            .Build(CallbackAction.WatchlistOpen, watchlistId);

        return new ScreenView(html, keyboard);
    }

    public async Task<ScreenView> PromptAsync(
        BotContext ctx,
        long watchlistId,
        PendingInputKind kind,
        CancellationToken ct)
    {
        var resolved = await access.ResolveAsync(ctx, watchlistId, ct);
        if (resolved.Watchlist is null)
            return Gone(ctx);

        if (!resolved.CanEdit)
            return (await RenderAsync(ctx, watchlistId, ct)).WithToast(BotTexts.Get(TextKey.NotAllowed, ctx.Language));

        sessions.Await(ctx.UserId, kind, watchlistId);

        var prompt = kind switch
        {
            PendingInputKind.FilterExcluded => TextKey.FilterExcludedPrompt,
            PendingInputKind.FilterLocations => TextKey.FilterLocationsPrompt,
            _ => TextKey.FilterKeywordsPrompt
        };

        var keyboard = new KeyboardBuilder(ctx.Language).Build(CallbackAction.FilterOpen, watchlistId);

        return new ScreenView($"<p>{BotTexts.Get(prompt, ctx.Language)}</p>", keyboard);
    }

    public async Task<ScreenView> ApplyListAsync(
        BotContext ctx,
        long watchlistId,
        PendingInputKind kind,
        string input,
        CancellationToken ct)
    {
        var resolved = await access.ResolveAsync(ctx, watchlistId, ct);
        if (resolved.Watchlist is not { } watchlist)
            return Gone(ctx);

        if (!resolved.CanEdit)
            return (await RenderAsync(ctx, watchlistId, ct)).WithToast(BotTexts.Get(TextKey.NotAllowed, ctx.Language));

        var values = BotFormatter.ParseList(input);

        var updated = kind switch
        {
            PendingInputKind.FilterExcluded => watchlist.Filter with { TitleNoneOf = values },
            PendingInputKind.FilterLocations => watchlist.Filter with { LocationAnyOf = values },
            _ => watchlist.Filter with { TitleAnyOf = values }
        };

        await watch.SetFilterAsync(watchlist, updated, ct);

        var view = await RenderAsync(ctx, watchlistId, ct);

        return view.WithToast(BotTexts.Get(TextKey.FilterSaved, ctx.Language));
    }

    public async Task<ScreenView> AskFreshnessAsync(BotContext ctx, long watchlistId, CancellationToken ct)
    {
        var resolved = await access.ResolveAsync(ctx, watchlistId, ct);
        if (resolved.Watchlist is null)
            return Gone(ctx);

        if (!resolved.CanEdit)
            return (await RenderAsync(ctx, watchlistId, ct)).WithToast(BotTexts.Get(TextKey.NotAllowed, ctx.Language));

        var builder = new KeyboardBuilder(ctx.Language);

        foreach (var days in FreshnessDays)
        {
            builder.Row(KeyboardBuilder.Make(
                BotTexts.Get(TextKey.FilterDays, ctx.Language, days),
                CallbackAction.FilterFreshnessSet,
                watchlistId,
                days));
        }

        builder.Row(KeyboardBuilder.Make(
            BotTexts.Get(TextKey.FilterFreshnessAny, ctx.Language),
            CallbackAction.FilterFreshnessSet,
            watchlistId,
            0));

        return new ScreenView(
            $"<p>{BotTexts.Get(TextKey.FilterFreshnessPrompt, ctx.Language)}</p>",
            builder.Build(CallbackAction.FilterOpen, watchlistId));
    }

    /// <summary>The freshness window travels in the callback's page slot - it is a small number, like a page.</summary>
    public async Task<ScreenView> SetFreshnessAsync(BotContext ctx, long watchlistId, int days, CancellationToken ct)
    {
        var resolved = await access.ResolveAsync(ctx, watchlistId, ct);
        if (resolved.Watchlist is not { } watchlist)
            return Gone(ctx);

        if (!resolved.CanEdit)
            return (await RenderAsync(ctx, watchlistId, ct)).WithToast(BotTexts.Get(TextKey.NotAllowed, ctx.Language));

        var updated = watchlist.Filter with { PostedWithinDays = days > 0 ? days : null };
        await watch.SetFilterAsync(watchlist, updated, ct);

        var view = await RenderAsync(ctx, watchlistId, ct);

        return view.WithToast(BotTexts.Get(TextKey.FilterSaved, ctx.Language));
    }

    public async Task<ScreenView> ClearAsync(BotContext ctx, long watchlistId, CancellationToken ct)
    {
        var resolved = await access.ResolveAsync(ctx, watchlistId, ct);
        if (resolved.Watchlist is not { } watchlist)
            return Gone(ctx);

        if (!resolved.CanEdit)
            return (await RenderAsync(ctx, watchlistId, ct)).WithToast(BotTexts.Get(TextKey.NotAllowed, ctx.Language));

        await watch.SetFilterAsync(watchlist, FilterSpec.MatchAll, ct);

        var view = await RenderAsync(ctx, watchlistId, ct);

        return view.WithToast(BotTexts.Get(TextKey.FilterCleared, ctx.Language));
    }

    private static ScreenView Gone(BotContext ctx)
    {
        var keyboard = new KeyboardBuilder(ctx.Language).Build(CallbackAction.MyWatchlists);

        return new ScreenView($"<p>{BotTexts.Get(TextKey.WatchlistGone, ctx.Language)}</p>", keyboard);
    }
}
