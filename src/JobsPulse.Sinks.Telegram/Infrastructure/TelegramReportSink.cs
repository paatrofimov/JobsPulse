using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Sinks.Telegram.Options;
using Microsoft.Extensions.Options;
using Telegram.Bot.Types;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Sinks.Telegram.Infrastructure;

/// <summary>
/// Sends statistics and run reports. A watchlist's report goes where its notifications go - to the owner, in the
/// owner's language, or to <c>Telegram:DefaultChatId</c> for a watchlist nobody owns (the routing of
/// <see cref="TelegramSink"/>). A discovery report belongs to no watchlist: it goes to the administrators among the
/// watchlist owners - an administrator is named by username, and the owners are the users the bot has a chat for -
/// and to the default chat only when none of them is known.
/// </summary>
public sealed class TelegramReportSink(
    TelegramClientFacade client,
    IBotUserStorage users,
    IWatchlistStorage watchlists,
    IOptionsMonitor<TelegramOptions> tgOpts,
    ILog log) : IReportSink
{
    private readonly ILog ctxLog = log.ForContext<TelegramReportSink>();

    public async Task<DeliveryResult> DeliverDigestAsync(Watchlist watchlist, WatchlistStats stats, CancellationToken ct)
    {
        var (chatId, language) = await RouteAsync(watchlist, ct);

        var links = DeepLinks.Companies(watchlist, await client.GetBotUsernameAsync(ct));

        return await SendAsync(chatId, StatsFormatter.Format(stats, language, digest: true, links), ct);
    }

    public async Task<DeliveryResult> DeliverRunAsync(
        Watchlist watchlist,
        TraversalRunReport report,
        CancellationToken ct)
    {
        var (chatId, language) = await RouteAsync(watchlist, ct);

        var links = DeepLinks.Companies(watchlist, await client.GetBotUsernameAsync(ct));

        return await SendAsync(chatId, StatsFormatter.FormatRun(report, language, links), ct);
    }

    public async Task<DeliveryResult> DeliverDiscoveryAsync(DiscoveryRunReport report, CancellationToken ct)
    {
        var targets = await AdministratorsAsync(ct);

        if (targets.Count == 0 && tgOpts.CurrentValue.DefaultChatId is { Length: > 0 } fallback)
            targets = [(fallback, BotLanguage.English)];

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

    private async Task<List<(string ChatId, BotLanguage Language)>> AdministratorsAsync(CancellationToken ct)
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
                .Select(u => (u.ChatId, u.Language))
                .Distinct()
        ];
    }

    private async Task<DeliveryResult> SendAsync(string chatId, string html, CancellationToken ct)
    {
        var result = await client.SendRichMessageAsync(chatId, new InputRichMessage { Html = html }, ct);

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
