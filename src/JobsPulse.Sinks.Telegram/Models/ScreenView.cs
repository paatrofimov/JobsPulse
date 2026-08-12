using Telegram.Bot.Types.ReplyMarkups;

namespace JobsPulse.Sinks.Telegram.Models;

/// <summary>
/// What a screen produces: the html body and its buttons. A screen never sends anything itself - the handler decides
/// whether this replaces the current message or starts a new one.
/// </summary>
/// <param name="Toast">Short confirmation for the callback popup («Saved»), shown without touching the screen body.</param>
public sealed record ScreenView(string Html, InlineKeyboardMarkup? Keyboard = null, string? Toast = null)
{
    public ScreenView WithToast(string? toast) => this with { Toast = toast };
}
