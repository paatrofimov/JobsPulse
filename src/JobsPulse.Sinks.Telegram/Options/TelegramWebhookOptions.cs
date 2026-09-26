namespace JobsPulse.Sinks.Telegram.Options;

public sealed class TelegramWebhookOptions
{
    public const string SectionName = "TelegramWebhook";

    /// <summary>Public base url of the webhook host, e.g. the Cloud Run service url. Read by the registration only.</summary>
    public string? PublicUrl { get; set; }

    public string Path { get; set; } = "/telegram";

    /// <summary>Echoed by Telegram in `X-Telegram-Bot-Api-Secret-Token` - the only proof a request came from Telegram.</summary>
    public string? SecretToken { get; set; }
}
