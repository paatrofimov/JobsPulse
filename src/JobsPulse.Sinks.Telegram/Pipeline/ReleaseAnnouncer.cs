using System.Text;
using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Sinks.Telegram.Infrastructure;
using JobsPulse.Sinks.Telegram.Infrastructure.Localization;
using JobsPulse.Sinks.Telegram.Options;
using Microsoft.Extensions.Options;
using Telegram.Bot.Types;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Sinks.Telegram.Pipeline;

/// <summary>
/// Tells the administrators, in their own chats and languages, that a new version is deployed and what has changed.
/// </summary>
public sealed class ReleaseAnnouncer(
    TelegramClientFacade client,
    IBotUserStorage users,
    IOptions<TelegramOptions> tgOptions,
    IOptions<ReleaseNoteOptions> noteOptions,
    ILog log)
{
    private readonly ILog ctxLog = log.ForContext<ReleaseAnnouncer>();

    public async Task AnnounceAsync(CancellationToken ct)
    {
        var note = noteOptions.Value;

        if (string.IsNullOrWhiteSpace(note.Version))
            throw new InvalidOperationException("ReleaseNote:Version is required");

        var tg = tgOptions.Value;

        // The username is stored only inside the display name, as «@username».
        var adminNames = tg.AdminUsernames.Select(x => "@" + x.TrimStart('@')).ToList();

        var admins = (await users.ListAsync(ct))
            .Where(x => adminNames.Contains(x.DisplayName, StringComparer.OrdinalIgnoreCase)
                        || tg.AdminChatIds.Contains(x.ChatId, StringComparer.Ordinal))
            .ToList();

        if (admins.Count == 0)
        {
            ctxLog.Warn("No administrator has talked to the bot yet - the release note is not sent");
            return;
        }

        var failed = 0;

        foreach (var admin in admins)
        {
            var html = Format(note, admin.Language);
            var result = await client.SendRichMessageAsync(admin.ChatId, new InputRichMessage { Html = html }, ct);

            if (result.Success)
                continue;

            failed++;
            ctxLog.Warn("Release note to chat {Chat} has failed: {Error}", admin.ChatId, result.Error);
        }

        if (failed == admins.Count)
            throw new InvalidOperationException("The release note has reached no administrator");

        ctxLog.Info("Release note {Version} is sent to {Count} administrator(s)", note.Version, admins.Count - failed);
    }

    private static string Format(ReleaseNoteOptions note, BotLanguage language)
    {
        var sb = new StringBuilder()
            .Append(BotTexts.Get(TextKey.ReleaseTitle, language, MessageFormatter.Escape(note.Version)));

        var changes = (note.Changes ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (changes.Length == 0)
            return sb.ToString();

        sb.Append("<br><br>").Append(BotTexts.Get(TextKey.ReleaseChanges, language));

        foreach (var change in changes)
            sb.Append("<br>• ").Append(MessageFormatter.Escape(change));

        return sb.ToString();
    }
}
