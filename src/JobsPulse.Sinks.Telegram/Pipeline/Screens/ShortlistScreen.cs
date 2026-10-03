using System.Text;
using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model.Domain;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Core.Options;
using JobsPulse.Sinks.Telegram.Infrastructure;
using JobsPulse.Sinks.Telegram.Infrastructure.Localization;
using JobsPulse.Sinks.Telegram.Models;
using Microsoft.Extensions.Options;
using Telegram.Bot.Types.ReplyMarkups;

namespace JobsPulse.Sinks.Telegram.Pipeline.Screens;

/// <summary>
/// Where to apply first (<see cref="Shortlist"/>): the top companies of a watchlist with their best vacancies, in one
/// of two slices - a <see cref="FocusArea"/>, or the vacancies published in the last days (3, 7 and 14 are buttons,
/// any other number is typed). Every company name is a link to all its vacancies. Read-only, like the statistics.
/// </summary>
public sealed class ShortlistScreen(
    WatchlistAccess access,
    IStateStore stateStore,
    VacancyPageBuilder pages,
    UserSessionStore sessions,
    TelegramClientFacade client,
    IOptionsMonitor<DigestOptions> options,
    TimeProvider clock)
{
    public static readonly int[] PeriodDays = [3, 7, 14];

    /// <summary>The whole feed is ranked, so the cap is far above the one of a browsable list.</summary>
    private const int MaxVacancies = 5000;

    public async Task<ScreenView> RenderFreshAsync(BotContext ctx, long watchlistId, int days, CancellationToken ct)
    {
        // A button carries its period in the page field; an old or forged one is brought back into range.
        days = days <= 0 ? StatsScreen.DefaultDays : Math.Min(days, options.CurrentValue.MaxPeriodDays);

        return await RenderAsync(
            ctx,
            watchlistId,
            BotTexts.Get(TextKey.ShortlistFresh, ctx.Language, StatsFormatter.Days(days, ctx.Language)),
            Shortlist.PublishedWithin(days, clock.GetUtcNow()),
            days,
            area: null,
            ct);
    }

    public async Task<ScreenView> RenderRegionAsync(BotContext ctx, long watchlistId, int area, CancellationToken ct)
    {
        var focus = Enum.IsDefined((FocusArea)area) ? (FocusArea)area : FocusArea.WesternEurope;

        return await RenderAsync(
            ctx,
            watchlistId,
            BotTexts.Get(TextKey.ShortlistRegion, ctx.Language, LocationRegions.Name(focus, ctx.Language)),
            v => LocationRegions.IsIn(v, focus),
            days: 0,
            focus,
            ct);
    }

    public async Task<ScreenView> PromptCustomAsync(BotContext ctx, long watchlistId, int days, CancellationToken ct)
    {
        var resolved = await access.ResolveAsync(ctx, watchlistId, ct);
        if (resolved.Watchlist is null)
            return Gone(ctx);

        return Prompt(ctx, watchlistId, days, TextKey.ShortlistCustomPrompt);
    }

    public async Task<ScreenView> ApplyCustomAsync(BotContext ctx, long watchlistId, string input, CancellationToken ct)
    {
        // A wrong answer keeps the step armed - it is usually a typo, not a change of mind.
        if (!StatsScreen.TryParseDays(input, options.CurrentValue.MaxPeriodDays, out var days))
            return Prompt(ctx, watchlistId, StatsScreen.DefaultDays, TextKey.StatsCustomInvalid);

        return await RenderFreshAsync(ctx, watchlistId, days, ct);
    }

    private async Task<ScreenView> RenderAsync(
        BotContext ctx,
        long watchlistId,
        string slice,
        Func<Vacancy, bool> inSlice,
        int days,
        FocusArea? area,
        CancellationToken ct)
    {
        var resolved = await access.ResolveAsync(ctx, watchlistId, ct);
        if (resolved.Watchlist is not { } watchlist)
            return Gone(ctx);

        var vacancies = await stateStore.LoadMatchedVacanciesAsync(watchlistId, MaxVacancies, ct);
        var companies = Shortlist.Build(watchlist, vacancies, inSlice);
        var bot = await client.GetBotUsernameAsync(ct);

        var sb = new StringBuilder(
            $"<h6>{BotTexts.Get(TextKey.ShortlistTitle, ctx.Language, MessageFormatter.Escape(watchlist.Name))}</h6>");

        sb.Append($"<p>{slice}<br>");
        sb.Append(BotTexts.Get(TextKey.ShortlistHint, ctx.Language, Shortlist.CompanyCount, Shortlist.VacanciesPerCompany));
        sb.Append("</p>");

        if (companies.Count == 0)
            sb.Append($"<p>{BotTexts.Get(TextKey.ShortlistEmpty, ctx.Language)}</p>");

        for (var i = 0; i < companies.Count; i++)
            AppendCompany(sb, i + 1, companies[i], bot, ctx.Language);

        return new ScreenView(sb.ToString(), Keyboard(ctx, watchlistId, days, area));
    }

    private void AppendCompany(StringBuilder sb, int place, ShortlistCompany company, string? bot, BotLanguage language)
    {
        var name = MessageFormatter.Escape(company.CompanyName);

        if (!string.IsNullOrEmpty(bot))
            name = $"<a href=\"{MessageFormatter.Escape(DeepLinks.Company(bot, company.EntryId))}\">{name}</a>";

        var worked = company.Worked ? $" · {BotTexts.Get(TextKey.ShortlistWorked, language)}" : string.Empty;

        sb.Append($"<p><b>{place}. {name}</b> · {BotTexts.Get(TextKey.ShortlistCompanyRow, language, company.Count)}{worked}</p>");

        foreach (var vacancy in company.Top)
            sb.Append(pages.RenderVacancy(vacancy, null, language));
    }

    /// <summary>The four areas on two rows and the periods on one, the current choice marked.</summary>
    private InlineKeyboardMarkup Keyboard(BotContext ctx, long watchlistId, int days, FocusArea? area)
    {
        InlineKeyboardButton AreaButton(FocusArea a) =>
            KeyboardBuilder.Make(
                Mark(LocationRegions.Name(a, ctx.Language), a == area), CallbackAction.ShortlistRegion, watchlistId, (int)a);

        var periods = PeriodDays
            .Select(d => KeyboardBuilder.Make(
                Mark(BotTexts.Get(TextKey.StatsDaysButton, ctx.Language, d), d == days),
                CallbackAction.ShortlistFresh,
                watchlistId,
                d))
            .Append(KeyboardBuilder.Make(
                Mark(BotTexts.Get(TextKey.StatsCustom, ctx.Language), days > 0 && !PeriodDays.Contains(days)),
                CallbackAction.ShortlistCustom,
                watchlistId,
                days))
            .ToArray();

        return new KeyboardBuilder(ctx.Language)
            .Row(AreaButton(FocusArea.WesternEurope), AreaButton(FocusArea.EasternEurope))
            .Row(AreaButton(FocusArea.Usa), AreaButton(FocusArea.Asia))
            .Row(periods)
            .Build(CallbackAction.WatchlistOpen, watchlistId);
    }

    private ScreenView Prompt(BotContext ctx, long watchlistId, int backDays, TextKey text)
    {
        sessions.Await(ctx.UserId, PendingInputKind.ShortlistDays, watchlistId);

        var keyboard = new KeyboardBuilder(ctx.Language).Build(CallbackAction.ShortlistFresh, watchlistId, backDays);

        return new ScreenView(
            $"<p>{BotTexts.Get(text, ctx.Language, options.CurrentValue.MaxPeriodDays)}</p>", keyboard);
    }

    private static string Mark(string label, bool current) =>
        current ? $"• {label} •" : label;

    private static ScreenView Gone(BotContext ctx)
    {
        var keyboard = new KeyboardBuilder(ctx.Language).Build(CallbackAction.MyWatchlists);

        return new ScreenView($"<p>{BotTexts.Get(TextKey.WatchlistGone, ctx.Language)}</p>", keyboard);
    }
}
