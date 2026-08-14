using JobsPulse.Sinks.Telegram.Infrastructure;
using JobsPulse.Sinks.Telegram.Infrastructure.Localization;
using JobsPulse.Sinks.Telegram.Models;
using JobsPulse.Sinks.Telegram.Pipeline.Screens;

namespace JobsPulse.Sinks.Telegram.Pipeline;

/// <summary>
/// Maps a button press to a screen, and an awaited free-text answer to the step that asked for it. One switch, so the
/// whole navigation graph is readable in one place; the screens themselves do the work.
/// </summary>
public sealed class ScreenRouter(
    MainMenuScreen menu,
    WatchlistsScreen watchlists,
    WatchlistScreen watchlist,
    FilterScreen filter,
    CompaniesScreen companies,
    DisabledCompaniesScreen disabled,
    AddCompanyScreen addCompany,
    VacanciesScreen vacancies,
    LanguageScreen language,
    AdminScreen admin)
{
    public async Task<(ScreenView View, BotContext Context)> RenderAsync(
        BotContext ctx,
        CallbackData data,
        CancellationToken ct)
    {
        // The language screen is the one action that changes the context it is rendered in.
        if (data.Action == CallbackAction.SetLanguage)
            return await language.SetAsync(ctx, data.Id, menu, ct);

        var view = await DispatchAsync(ctx, data, ct);

        return (view, ctx);
    }

    private async Task<ScreenView> DispatchAsync(BotContext ctx, CallbackData data, CancellationToken ct) =>
        data.Action switch
        {
            CallbackAction.Menu => menu.Render(ctx),
            CallbackAction.Help => menu.RenderHelp(ctx),

            CallbackAction.MyWatchlists => await watchlists.RenderMineAsync(ctx, data.Page, ct),
            CallbackAction.AllWatchlists => await watchlists.RenderAllAsync(ctx, data.Page, ct),

            CallbackAction.WatchlistNew => watchlist.PromptCreate(ctx),
            CallbackAction.WatchlistOpen => await watchlist.RenderAsync(ctx, data.Id, ct),
            CallbackAction.WatchlistRename => await watchlist.PromptRenameAsync(ctx, data.Id, ct),
            CallbackAction.WatchlistTogglePaused => await watchlist.TogglePausedAsync(ctx, data.Id, ct),
            CallbackAction.WatchlistDeleteAsk => await watchlist.ConfirmDeleteAsync(ctx, data.Id, ct),
            CallbackAction.WatchlistDelete => await watchlist.DeleteAsync(ctx, data.Id, watchlists, ct),

            CallbackAction.FilterOpen => await filter.RenderAsync(ctx, data.Id, ct),
            CallbackAction.FilterKeywords =>
                await filter.PromptAsync(ctx, data.Id, PendingInputKind.FilterKeywords, ct),
            CallbackAction.FilterExcluded =>
                await filter.PromptAsync(ctx, data.Id, PendingInputKind.FilterExcluded, ct),
            CallbackAction.FilterLocations =>
                await filter.PromptAsync(ctx, data.Id, PendingInputKind.FilterLocations, ct),
            CallbackAction.FilterLocationsExcluded =>
                await filter.PromptAsync(ctx, data.Id, PendingInputKind.FilterLocationsExcluded, ct),
            CallbackAction.FilterDescription =>
                await filter.PromptAsync(ctx, data.Id, PendingInputKind.FilterDescription, ct),
            CallbackAction.FilterDescriptionExcluded =>
                await filter.PromptAsync(ctx, data.Id, PendingInputKind.FilterDescriptionExcluded, ct),
            CallbackAction.FilterFreshnessAsk => await filter.AskFreshnessAsync(ctx, data.Id, ct),
            CallbackAction.FilterFreshnessSet => await filter.SetFreshnessAsync(ctx, data.Id, data.Page, ct),
            CallbackAction.FilterClear => await filter.ClearAsync(ctx, data.Id, ct),

            CallbackAction.CompaniesOpen => await companies.RenderAsync(ctx, data.Id, data.Page, ct),
            CallbackAction.CompaniesByLocation =>
                await companies.RenderAsync(ctx, data.Id, data.Page, ct, VacancyGrouping.Location),
            CallbackAction.CompanyOpen => await companies.RenderCompanyAsync(ctx, data.Id, data.Page, ct),
            CallbackAction.CompanyToggleWorked => await companies.ToggleWorkedAsync(ctx, data.Id, data.Page, ct),
            CallbackAction.CompanyToggleEnabled => await companies.ToggleEnabledAsync(ctx, data.Id, data.Page, ct),
            CallbackAction.CompanyRemove => await companies.RemoveAsync(ctx, data.Id, data.Page, ct),
            CallbackAction.CompanyFind => await companies.PromptFindAsync(ctx, data.Id, data.Page, ct),
            CallbackAction.CompanyAdd => await addCompany.PromptAsync(ctx, data.Id, ct),
            CallbackAction.CompanyPick => await addCompany.PickAsync(ctx, data.Id, ct),

            CallbackAction.DisabledCompanies => await disabled.RenderAsync(ctx, data.Page, ct),
            CallbackAction.CompanyRestore => await disabled.RestoreAsync(ctx, data.Id, data.Page, ct),

            CallbackAction.VacanciesPick => await vacancies.RenderPickAsync(ctx, data.Page, ct),
            CallbackAction.VacanciesOpen => await vacancies.RenderAsync(ctx, data.Id, data.Page, ct),
            CallbackAction.VacanciesByLocation =>
                await vacancies.RenderAsync(ctx, data.Id, data.Page, ct, VacancyGrouping.Location),

            CallbackAction.Language => language.Render(ctx),
            CallbackAction.Admin => await admin.RenderAsync(ctx, ct),

            // A stale button from an old message, or the page label, which is a button only because rows need one.
            _ => menu.Render(ctx)
        };

    /// <summary>Routes the answer to whatever step armed the session. Nothing armed means it was a stray message.</summary>
    public async Task<ScreenView?> HandleTextAsync(
        BotContext ctx,
        UserSession session,
        string text,
        CancellationToken ct) =>
        session.Pending switch
        {
            PendingInputKind.WatchlistName => await watchlist.CreateAsync(ctx, text, ct),
            PendingInputKind.WatchlistRename => await watchlist.RenameAsync(ctx, session.WatchlistId, text, ct),
            PendingInputKind.FilterKeywords =>
                await filter.ApplyListAsync(ctx, session.WatchlistId, PendingInputKind.FilterKeywords, text, ct),
            PendingInputKind.FilterExcluded =>
                await filter.ApplyListAsync(ctx, session.WatchlistId, PendingInputKind.FilterExcluded, text, ct),
            PendingInputKind.FilterLocations =>
                await filter.ApplyListAsync(ctx, session.WatchlistId, PendingInputKind.FilterLocations, text, ct),
            PendingInputKind.FilterLocationsExcluded => await filter.ApplyListAsync(
                ctx, session.WatchlistId, PendingInputKind.FilterLocationsExcluded, text, ct),
            PendingInputKind.FilterDescription => await filter.ApplyListAsync(
                ctx, session.WatchlistId, PendingInputKind.FilterDescription, text, ct),
            PendingInputKind.FilterDescriptionExcluded => await filter.ApplyListAsync(
                ctx, session.WatchlistId, PendingInputKind.FilterDescriptionExcluded, text, ct),
            PendingInputKind.CompanyQuery => await addCompany.SearchAsync(ctx, session.WatchlistId, text, ct),
            PendingInputKind.CompanyName => await companies.FindAsync(ctx, session.WatchlistId, text, ct),
            _ => null
        };

    public ScreenView UnknownCommand(BotContext ctx)
    {
        var view = menu.Render(ctx);

        return new ScreenView(
            $"<p>{BotTexts.Get(TextKey.UnknownCommand, ctx.Language)}</p>{view.Html}", view.Keyboard);
    }
}
