namespace JobsPulse.Sinks.Telegram.Options;

public sealed class TelegramOptions
{
    public const string SectionName = "Telegram";

    // SENSITIVE
    public string BotFatherToken { get; set; } = "";

    // For now single chat is enough
    public required string DefaultChatId { get; set; }

    // Without this list anyone can change watchlist
    public List<string> AdminChatIds { get; set; } = [];

    // /watch, /list, ...
    public bool EnableCommands { get; set; } = true;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BotFatherToken);
}