using System.Globalization;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Core.Options;
using JobsPulse.Core.Pipeline;
using JobsPulse.Sinks.Telegram.Infrastructure;
using JobsPulse.Sinks.Telegram.Infrastructure.Localization;
using JobsPulse.Sinks.Telegram.Models;
using Microsoft.Extensions.Options;

namespace JobsPulse.Sinks.Telegram.Pipeline.Screens;

/// <summary>
/// Statistics of one watchlist for a period ending now, counted when the button is pressed. Three periods are
/// buttons; any other number of days is typed. Read-only, so somebody else's watchlist shows it too.
/// </summary>
public sealed class StatsScreen(
    WatchlistAccess access,
    WatchlistStatsService stats,
    UserSessionStore sessions,
    IOptionsMonitor<DigestOptions> options)
{
    public const int DefaultDays = 7;

    public static readonly int[] PeriodDays = [7, 14, 30];

    public async Task<ScreenView> RenderAsync(BotContext ctx, long watchlistId, int days, CancellationToken ct)
    {
        var resolved = await access.ResolveAsync(ctx, watchlistId, ct);
        if (resolved.Watchlist is not { } watchlist)
            return Gone(ctx);

        // A button carries its period in the page field; an old or forged one is brought back into range.
        days = days <= 0 ? DefaultDays : Math.Min(days, options.CurrentValue.MaxPeriodDays);

        var result = await stats.ComputeAsync(watchlist, days, ct);

        var periods = PeriodDays
            .Select(d => KeyboardBuilder.Make(
                PeriodLabel(d, d == days, ctx.Language), CallbackAction.StatsOpen, watchlistId, d))
            .ToArray();

        var keyboard = new KeyboardBuilder(ctx.Language)
            .Row(periods)
            .Button(TextKey.StatsCustom, CallbackAction.StatsCustom, watchlistId, days)
            .Build(CallbackAction.WatchlistOpen, watchlistId);

        return new ScreenView(StatsFormatter.Format(result, ctx.Language, digest: false), keyboard);
    }

    public async Task<ScreenView> PromptCustomAsync(BotContext ctx, long watchlistId, int days, CancellationToken ct)
    {
        var resolved = await access.ResolveAsync(ctx, watchlistId, ct);
        if (resolved.Watchlist is null)
            return Gone(ctx);

        return Prompt(ctx, watchlistId, days, TextKey.StatsCustomPrompt);
    }

    public async Task<ScreenView> ApplyCustomAsync(BotContext ctx, long watchlistId, string input, CancellationToken ct)
    {
        // A wrong answer keeps the step armed - it is usually a typo, not a change of mind.
        if (!TryParseDays(input, options.CurrentValue.MaxPeriodDays, out var days))
            return Prompt(ctx, watchlistId, DefaultDays, TextKey.StatsCustomInvalid);

        return await RenderAsync(ctx, watchlistId, days, ct);
    }

    public static bool TryParseDays(string input, int maxDays, out int days) =>
        int.TryParse(input.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out days)
        && days >= 1
        && days <= maxDays;

    private ScreenView Prompt(BotContext ctx, long watchlistId, int backDays, TextKey text)
    {
        sessions.Await(ctx.UserId, PendingInputKind.StatsDays, watchlistId);

        var keyboard = new KeyboardBuilder(ctx.Language).Build(CallbackAction.StatsOpen, watchlistId, backDays);

        return new ScreenView(
            $"<p>{BotTexts.Get(text, ctx.Language, options.CurrentValue.MaxPeriodDays)}</p>", keyboard);
    }

    private static string PeriodLabel(int days, bool current, BotLanguage language)
    {
        var label = BotTexts.Get(TextKey.StatsDaysButton, language, days);

        return current ? $"• {label} •" : label;
    }

    private static ScreenView Gone(BotContext ctx)
    {
        var keyboard = new KeyboardBuilder(ctx.Language).Build(CallbackAction.MyWatchlists);

        return new ScreenView($"<p>{BotTexts.Get(TextKey.WatchlistGone, ctx.Language)}</p>", keyboard);
    }
}
