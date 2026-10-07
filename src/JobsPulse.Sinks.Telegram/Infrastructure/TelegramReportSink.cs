using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Sinks.Telegram.Infrastructure.Localization;
using JobsPulse.Sinks.Telegram.Models;
using JobsPulse.Sinks.Telegram.Options;
using Microsoft.Extensions.Options;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Sinks.Telegram.Infrastructure;

/// <summary>
/// Sends statistics and run reports. A watchlist's report goes where its notifications go - to the owner, in the
/// owner's language, or to <c>Telegram:DefaultChatId</c> for a watchlist nobody owns (the routing of
/// <see cref="TelegramSink"/>). A discovery report belongs to no watchlist: it goes to the administrators among the
/// watchlist owners - an administrator is named by username, and the owners are the users the bot has a chat for -
/// and to the default chat only when none of them is known. An administrator in silent mode gets none, and no run
/// report reaches a chat whose user is in silent mode - even one routed to the default chat.
/// </summary>
public sealed class TelegramReportSink(
    TelegramClientFacade client,
    IBotUserStorage users,
    IWatchlistStorage watchlists,
    IOptionsMonitor<TelegramOptions> tgOpts,
    ILog log) : IReportSink
{
    private readonly ILog ctxLog = log.ForContext<TelegramReportSink>();

    public async Task<DeliveryResult> DeliverDigestAsync(
        Watchlist watchlist,
        WatchlistStats stats,
        WatchlistDigest digest,
        CancellationToken ct)
    {
        var (chatId, language) = await RouteAsync(watchlist, ct);

        var links = DeepLinks.Companies(watchlist, await client.GetBotUsernameAsync(ct));
        var html = StatsFormatter.Format(stats, language, digest: true, links, sincePrevious: digest.PreviousId is not null);

        var narrowed = new List<(TextKey Label, CallbackAction Action)>();
        if (stats.Opened > 0)
            narrowed.Add((TextKey.DigestOpenedButton, CallbackAction.DigestOpenedOpen));
        if (stats.Closed + stats.Dropped > 0)
            narrowed.Add((TextKey.DigestClosedButton, CallbackAction.DigestClosedOpen));

        var keyboard = new KeyboardBuilder(language)
            .Button(TextKey.DigestAllChanges, CallbackAction.DigestChangesOpen, digest.Id)
            .Modes(narrowed, digest.Id)
            .BuildBare();

        return await SendAsync(chatId, html, ct, keyboard);
    }

    public async Task<DeliveryResult> DeliverRunAsync(
        Watchlist watchlist,
        TraversalRunReport report,
        CancellationToken ct)
    {
        var (chatId, language) = await RouteAsync(watchlist, ct);

        // The owner filter of RunReportService misses a watchlist nobody owns: its report lands in the default chat.
        if (await users.IsChatSilentAsync(chatId, ct))
        {
            ctxLog.Info("Run report of watchlist {Watchlist} is muted: chat {Chat} is in silent mode", watchlist.Id, chatId);

            return DeliveryResult.Ok;
        }

        var links = DeepLinks.Companies(watchlist, await client.GetBotUsernameAsync(ct));

        return await SendAsync(chatId, StatsFormatter.FormatRun(report, language, links), ct);
    }

    public async Task<DeliveryResult> DeliverDiscoveryAsync(DiscoveryRunReport report, CancellationToken ct)
    {
        var administrators = await AdministratorsAsync(ct);

        var targets = administrators
            .Where(u => !u.SilentMode)
            .Select(u => (u.ChatId, u.Language))
            .Distinct()
            .ToList();

        // Silent administrators are known - the default chat stands in only when nobody is.
        if (administrators.Count == 0 && tgOpts.CurrentValue.DefaultChatId is { Length: > 0 } fallback)
        {
            if (await users.IsChatSilentAsync(fallback, ct))
                return DeliveryResult.Ok;

            targets = [(fallback, BotLanguage.English)];
        }

        if (administrators.Count > 0 && targets.Count == 0)
            return DeliveryResult.Ok;

        if (targets.Count == 0)
            return DeliveryResult.Fail("no administrator chat is known");

        foreach (var (chatId, language) in targets)
        {
            var result = await SendAsync(chatId, StatsFormatter.FormatDiscovery(report, language), ct);
            if (!result.Success)
                return result;
        }

        return DeliveryResult.Ok;
    }

    private async Task<List<BotUser>> AdministratorsAsync(CancellationToken ct)
    {
        var ownerIds = (await watchlists.GetAllAsync(ct))
            .Select(w => w.OwnerUserId)
            .OfType<long>()
            .Distinct()
            .ToList();

        var owners = await users.GetManyAsync(ownerIds, ct);
        var opts = tgOpts.CurrentValue;

        return
        [
            .. owners.Values
                // Only an «@name» display name is a username - a first name could match by accident.
                .Where(u => opts.IsAdmin(
                    u.DisplayName is ['@', .. var username] ? username : null,
                    u.ChatId))
        ];
    }

    private async Task<DeliveryResult> SendAsync(
        string chatId,
        string html,
        CancellationToken ct,
        ReplyMarkup? keyboard = null)
    {
        var result = await client.SendRichMessageAsync(chatId, new InputRichMessage { Html = html }, ct, keyboard);

        return result.Success
            ? DeliveryResult.Ok
            : DeliveryResult.Fail(result.Error ?? "unknown", result.RetryAfter);
    }

    private async Task<(string ChatId, BotLanguage Language)> RouteAsync(Watchlist watchlist, CancellationToken ct)
    {
        var fallback = (tgOpts.CurrentValue.DefaultChatId, BotLanguage.English);

        if (watchlist.OwnerUserId is not { } ownerId)
            return fallback;

        var owners = await users.GetManyAsync([ownerId], ct);
        if (owners.TryGetValue(ownerId, out var owner))
            return (owner.ChatId, owner.Language);

        ctxLog.Warn("Owner {Owner} of watchlist {Watchlist} is unknown — using the default chat", ownerId, watchlist.Id);

        return fallback;
    }
}
