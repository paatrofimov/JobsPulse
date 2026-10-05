using JobsPulse.Core.Abstractions;
using JobsPulse.Sinks.Telegram.Infrastructure.Localization;
using JobsPulse.Sinks.Telegram.Models;

namespace JobsPulse.Sinks.Telegram.Pipeline.Screens;

/// <summary>
/// The silent mode switch of the main menu. Stored on the user, because the reports it mutes are sent by the jobs on
/// another host.
/// </summary>
public sealed class SilentModeScreen(IBotUserStorage users)
{
    /// <summary>Returns the menu with the button already showing the new state.</summary>
    public async Task<(ScreenView View, BotContext Context)> ToggleAsync(
        BotContext ctx,
        MainMenuScreen menu,
        CancellationToken ct)
    {
        var silent = !ctx.User.SilentMode;

        await users.SetSilentModeAsync(ctx.UserId, silent, ct);

        var updated = ctx with { User = ctx.User with { SilentMode = silent } };
        var toast = BotTexts.Get(silent ? TextKey.SilentModeOn : TextKey.SilentModeOff, ctx.Language);

        return (menu.Render(updated).WithToast(toast), updated);
    }
}
