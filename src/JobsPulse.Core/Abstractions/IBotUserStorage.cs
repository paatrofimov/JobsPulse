using JobsPulse.Core.Model.Infrastructure;

namespace JobsPulse.Core.Abstractions;

/// <summary>
/// The people using the bot: who they are, where to write to them and in which language. Persistent, because a
/// watchlist owner has to stay resolvable across restarts and because the language is a setting, not a session.
/// </summary>
public interface IBotUserStorage
{
    /// <summary>
    /// Called on every incoming update: inserts an unknown user and refreshes the chat id, the display name and the
    /// last-seen stamp of a known one. The language is never touched here - only the user changes it.
    /// </summary>
    Task<BotUser> UpsertOnContactAsync(
        long telegramUserId,
        string chatId,
        string? displayName,
        BotLanguage fallbackLanguage,
        CancellationToken ct);

    Task<BotUser?> GetAsync(long telegramUserId, CancellationToken ct);

    /// <summary>Owners of a watchlist listing, resolved in one query instead of one per row.</summary>
    Task<IReadOnlyDictionary<long, BotUser>> GetManyAsync(IReadOnlyList<long> telegramUserIds, CancellationToken ct);

    Task<bool> SetLanguageAsync(long telegramUserId, BotLanguage language, CancellationToken ct);
}
