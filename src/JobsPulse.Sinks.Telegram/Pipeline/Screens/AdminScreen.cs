using JobsPulse.Sinks.Telegram.Infrastructure;
using JobsPulse.Sinks.Telegram.Infrastructure.Localization;
using JobsPulse.Sinks.Telegram.Models;

namespace JobsPulse.Sinks.Telegram.Pipeline.Screens;

/// <summary>
/// The door to the operator commands. It is deliberately a list of slash commands rather than buttons: these actions
/// take free-form arguments, are rare and are destructive enough that typing them out is a feature. An ordinary user
/// never sees this screen.
/// </summary>
public sealed class AdminScreen
{
    public ScreenView Render(BotContext ctx)
    {
        if (!ctx.IsAdmin)
        {
            return new ScreenView(
                $"<p>{BotTexts.Get(TextKey.AdminOnly, ctx.Language)}</p>",
                new KeyboardBuilder(ctx.Language).Build(CallbackAction.Menu));
        }

        var commands = AdminCommandCatalog.All
            .Select(c => $"/{c.Command} — {MessageFormatter.Escape(c.Description)}");

        var html = "<h6>🛠 Admin</h6>"
                   + $"<p>{string.Join("<br>", commands)}</p>"
                   + "<p>These commands take raw ids and json — they are the operator surface, not the user one.</p>";

        return new ScreenView(html, new KeyboardBuilder(ctx.Language).Build(CallbackAction.Menu));
    }
}
