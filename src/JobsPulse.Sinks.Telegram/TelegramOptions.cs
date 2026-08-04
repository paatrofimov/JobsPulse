namespace JobsPulse.Sinks.Telegram;

public sealed class TelegramOptions
{
    public const string SectionName = "Telegram";

    /// <summary>
    /// Токен от @BotFather. НИКОГДА не хранить в appsettings.json —
    /// только user-secrets локально и переменная окружения Telegram__BotToken в проде.
    /// </summary>
    public string BotToken { get; set; } = "";

    /// <summary>Чат по умолчанию, куда падают уведомления.</summary>
    public string? DefaultChatId { get; set; }

    /// <summary>
    /// Кому разрешено управлять ботом. Пустой список = команды отключены.
    /// Бот публичен по своей природе — без этого списка любой желающий сможет менять ваш watchlist.
    /// </summary>
    public List<string> AdminChatIds { get; set; } = [];

    /// <summary>Слушать ли команды (/watch, /list, ...). Отправка уведомлений от этого не зависит.</summary>
    public bool EnableCommands { get; set; } = true;

    public string ApiBaseUrl { get; set; } = "https://api.telegram.org";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BotToken);
}
