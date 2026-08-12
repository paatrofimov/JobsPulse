using JobsPulse.Core.Model.Infrastructure;

namespace JobsPulse.Storage.PersistentModels;

/// <summary>Table `bot_user` - one row per person talking to the bot, keyed by the telegram user id.</summary>
public class PersistentBotUser
{
    public long Id { get; set; }

    /// <summary>The identity a watchlist owner is stored as. Unique.</summary>
    public long TelegramUserId { get; set; }

    public required string ChatId { get; set; }

    public string? DisplayName { get; set; }

    /// <summary>Stored as int, so reordering the enum breaks nothing.</summary>
    public BotLanguage Language { get; set; } = BotLanguage.English;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }
}
