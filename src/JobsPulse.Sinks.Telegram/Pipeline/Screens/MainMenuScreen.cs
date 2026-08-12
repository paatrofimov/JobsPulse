using JobsPulse.Sinks.Telegram.Infrastructure;
using JobsPulse.Sinks.Telegram.Infrastructure.Localization;
using JobsPulse.Sinks.Telegram.Models;

namespace JobsPulse.Sinks.Telegram.Pipeline.Screens;

/// <summary>
/// The root screen and the answer to /start. It explains what the bot is for in two sentences, because a first-time
/// user has no idea what a «watchlist» is, and offers every entry point as a button - no command has to be typed.
/// </summary>
public sealed class MainMenuScreen
{
    public ScreenView Render(BotContext ctx)
    {
        var html = $"<h6>{BotTexts.Get(TextKey.MenuTitle, ctx.Language)}</h6>"
                   + $"<p>{BotTexts.Get(TextKey.MenuGreeting, ctx.Language)}</p>";

        var keyboard = new KeyboardBuilder(ctx.Language)
            .Button(TextKey.MenuMyWatchlists, CallbackAction.MyWatchlists)
            .Button(TextKey.MenuVacancies, CallbackAction.VacanciesPick)
            .Button(TextKey.MenuDisabledCompanies, CallbackAction.DisabledCompanies)
            .Button(TextKey.MenuAllWatchlists, CallbackAction.AllWatchlists)
            .Button(TextKey.MenuLanguage, CallbackAction.Language)
            .Button(TextKey.MenuHelp, CallbackAction.Help)
            .ButtonIf(ctx.IsAdmin, TextKey.MenuAdmin, CallbackAction.Admin)
            .BuildBare();

        return new ScreenView(html, keyboard);
    }

    public ScreenView RenderHelp(BotContext ctx)
    {
        var keyboard = new KeyboardBuilder(ctx.Language).Build(CallbackAction.Menu);

        return new ScreenView($"<p>{BotTexts.Get(TextKey.Help, ctx.Language)}</p>", keyboard);
    }
}
