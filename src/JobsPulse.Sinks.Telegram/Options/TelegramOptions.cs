namespace JobsPulse.Sinks.Telegram.Options;

public sealed class TelegramOptions
{
    public const string SectionName = "Telegram";

    /// <summary>Fallback delivery target: notifications of a watchlist that has no owner land here.</summary>
    public required string DefaultChatId { get; set; } = "294142919";

    /// <summary>
    /// Telegram usernames (without the leading @, case-insensitive) that unlock the admin section. This is the
    /// primary way to name an administrator: a username is what a person knows about themselves, a numeric chat id is
    /// not. Every ownerless watchlist is claimed by the first administrator who talks to the bot.
    /// </summary>
    public List<string> AdminUsernames { get; set; } = ["patrofimov"];

    /// <summary>
    /// Chats that also unlock the admin section, for an administrator whose username is not set. Ordinary users are
    /// not listed here and never see it - this is not the list of who may talk to the bot,
    /// see <see cref="AllowedUserIds"/>.
    /// </summary>
    public List<string> AdminChatIds { get; set; } = [];

    /// <summary>
    /// Telegram user ids allowed to use the bot at all. Empty (the default) means everybody: watchlists are owned
    /// per user, so a stranger can only ever create and edit their own. Fill it in to lock the bot down.
    /// </summary>
    public List<long> AllowedUserIds { get; set; } = [];

    public bool EnableCommands { get; set; } = true;

    public bool IsAdmin(string? username, string chatId) =>
        (username is { Length: > 0 }
         && AdminUsernames.Any(x => string.Equals(x.TrimStart('@'), username, StringComparison.OrdinalIgnoreCase)))
        || AdminChatIds.Contains(chatId, StringComparer.Ordinal);
}
