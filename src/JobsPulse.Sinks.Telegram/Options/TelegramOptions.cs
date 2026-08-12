namespace JobsPulse.Sinks.Telegram.Options;

public sealed class TelegramOptions
{
    public const string SectionName = "Telegram";

    /// <summary>Fallback delivery target: notifications of a watchlist that has no owner land here.</summary>
    public required string DefaultChatId { get; set; } = "294142919";

    /// <summary>
    /// Chats that unlock the admin section. Ordinary users are not listed here and never see it - this is not the
    /// list of who may talk to the bot, see <see cref="AllowedUserIds"/>.
    /// </summary>
    public List<string> AdminChatIds { get; set; } = ["294142919"];

    /// <summary>
    /// Telegram user ids allowed to use the bot at all. Empty (the default) means everybody: watchlists are owned
    /// per user, so a stranger can only ever create and edit their own. Fill it in to lock the bot down.
    /// </summary>
    public List<long> AllowedUserIds { get; set; } = [];

    public bool EnableCommands { get; set; } = true;
}
