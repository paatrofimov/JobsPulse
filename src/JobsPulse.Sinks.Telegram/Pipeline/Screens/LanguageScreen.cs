using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Sinks.Telegram.Infrastructure;
using JobsPulse.Sinks.Telegram.Infrastructure.Localization;
using JobsPulse.Sinks.Telegram.Models;

namespace JobsPulse.Sinks.Telegram.Pipeline.Screens;

/// <summary>
/// The language switch. The choice is stored on the user, not on the session, so it also applies to the notifications
/// that arrive hours later.
/// </summary>
public sealed class LanguageScreen(IBotUserStorage users)
{
    public ScreenView Render(BotContext ctx)
    {
        var builder = new KeyboardBuilder(ctx.Language);

        foreach (var language in (BotLanguage[])[BotLanguage.English, BotLanguage.Russian])
        {
            var mark = language == ctx.Language ? " ✓" : string.Empty;

            builder.Row(KeyboardBuilder.Make(
                $"{BotTexts.LanguageName(language)}{mark}", CallbackAction.SetLanguage, (long)language));
        }

        return new ScreenView(
            $"<h6>{BotTexts.Get(TextKey.LanguageTitle, ctx.Language)}</h6>",
            builder.Build(CallbackAction.Menu));
    }

    /// <summary>Returns the context with the new language, so the very answer is already localized.</summary>
    public async Task<(ScreenView View, BotContext Context)> SetAsync(
        BotContext ctx,
        long language,
        MainMenuScreen menu,
        CancellationToken ct)
    {
        var chosen = language == (long)BotLanguage.Russian ? BotLanguage.Russian : BotLanguage.English;

        await users.SetLanguageAsync(ctx.UserId, chosen, ct);

        var updated = ctx with { User = ctx.User with { Language = chosen } };

        return (menu.Render(updated).WithToast(BotTexts.Get(TextKey.LanguageChanged, chosen)), updated);
    }
}
