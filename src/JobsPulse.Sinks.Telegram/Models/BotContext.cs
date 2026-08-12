using JobsPulse.Core.Model.Infrastructure;

namespace JobsPulse.Sinks.Telegram.Models;

/// <summary>Who the current update belongs to. Passed to every screen instead of a bare chat id.</summary>
public sealed record BotContext
{
    public required BotUser User { get; init; }

    /// <summary>The chat this update arrived in - not necessarily the one stored on the user.</summary>
    public required string ChatId { get; init; }

    /// <summary>
    /// The user is named in <c>Telegram:AdminUsernames</c> (or their chat in <c>Telegram:AdminChatIds</c>), so the
    /// admin section is reachable.
    /// </summary>
    public required bool IsAdmin { get; init; }

    public long UserId => User.TelegramUserId;

    public BotLanguage Language => User.Language;
}
