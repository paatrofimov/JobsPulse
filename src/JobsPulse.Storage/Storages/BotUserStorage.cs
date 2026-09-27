using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Storage.PersistentModels;
using Microsoft.EntityFrameworkCore;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Storage.Storages;

/// <summary>
/// Pure EF: the user table is tiny and read one row at a time. Every incoming update touches it, so the upsert is
/// deliberately the cheapest thing here - one lookup by the unique telegram user id and a write only when something
/// actually changed.
/// </summary>
internal class BotUserStorage(
    IDbContextFactory<JobsPulseDbContext> factory,
    TimeProvider clock,
    ILog log) : IBotUserStorage
{
    private readonly ILog ctxLog = log.ForContext<BotUserStorage>();

    public async Task<BotUser> UpsertOnContactAsync(
        long telegramUserId,
        string chatId,
        string? displayName,
        BotLanguage fallbackLanguage,
        CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var now = clock.GetUtcNow();

        var row = await db.BotUsers.FirstOrDefaultAsync(x => x.TelegramUserId == telegramUserId, ct);

        if (row is null)
        {
            row = new PersistentBotUser
            {
                TelegramUserId = telegramUserId,
                ChatId = chatId,
                DisplayName = displayName,
                Language = fallbackLanguage,
                CreatedAt = now,
                LastSeenAt = now
            };

            db.BotUsers.Add(row);

            try
            {
                await db.SaveChangesAsync(ct);
                ctxLog.Info("New bot user {User} ({Name})", telegramUserId, displayName);

                return row.ToDomainModel();
            }
            catch (DbUpdateException ex)
            {
                // Two updates from the same brand new user can race - the unique index decides, then re-read.
                ctxLog.Warn(ex, "Bot user {User} was inserted concurrently", telegramUserId);

                return await GetAsync(telegramUserId, ct)
                       ?? row.ToDomainModel();
            }
        }

        // The language is a setting and is never overwritten here - only the user changes it.
        row.ChatId = chatId;
        row.LastSeenAt = now;

        if (!string.IsNullOrWhiteSpace(displayName))
            row.DisplayName = displayName;

        await db.SaveChangesAsync(ct);

        return row.ToDomainModel();
    }

    public async Task<BotUser?> GetAsync(long telegramUserId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var row = await db.BotUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TelegramUserId == telegramUserId, ct);

        return row?.ToDomainModel();
    }

    public async Task<IReadOnlyDictionary<long, BotUser>> GetManyAsync(
        IReadOnlyList<long> telegramUserIds,
        CancellationToken ct)
    {
        if (telegramUserIds.Count == 0)
            return new Dictionary<long, BotUser>();

        await using var db = await factory.CreateDbContextAsync(ct);

        var rows = await db.BotUsers
            .AsNoTracking()
            .Where(x => telegramUserIds.Contains(x.TelegramUserId))
            .ToListAsync(ct);

        return rows.ToDictionary(x => x.TelegramUserId, x => x.ToDomainModel());
    }

    public async Task<bool> SetLanguageAsync(long telegramUserId, BotLanguage language, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var affected = await db.BotUsers
            .Where(x => x.TelegramUserId == telegramUserId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Language, language), ct);

        return affected > 0;
    }
}
