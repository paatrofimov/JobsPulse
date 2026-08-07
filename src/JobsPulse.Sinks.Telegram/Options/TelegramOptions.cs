namespace JobsPulse.Sinks.Telegram.Options;

public sealed class TelegramOptions
{
    public const string SectionName = "Telegram";

    // For now single chat is enough
    public required string DefaultChatId { get; set; } = "294142919";

    // Without this list anyone can change watchlist
    public List<string> AdminChatIds { get; set; } = ["294142919"];

    // /watch, /list, ...
    public bool EnableCommands { get; set; } = true;
}