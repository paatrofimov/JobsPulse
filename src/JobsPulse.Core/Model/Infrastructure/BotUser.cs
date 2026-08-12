namespace JobsPulse.Core.Model.Infrastructure;

/// <summary>
/// One person talking to the bot. The telegram user id is the identity: it owns watchlists and survives a chat being
/// recreated, which a chat id does not.
/// </summary>
public sealed record BotUser
{
    public required long TelegramUserId { get; init; }

    /// <summary>Where to deliver notifications. Refreshed on every contact - a user may write from a new chat.</summary>
    public required string ChatId { get; init; }

    /// <summary>Username or first name, only ever shown as the owner of a watchlist.</summary>
    public string? DisplayName { get; init; }

    public BotLanguage Language { get; init; } = BotLanguage.English;

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset LastSeenAt { get; init; }
}
