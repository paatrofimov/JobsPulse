using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model.Domain;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Core.Options;
using JobsPulse.Sinks.Telegram.Options;
using Microsoft.Extensions.Options;
using Telegram.Bot.Types;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Sinks.Telegram.Infrastructure;

/// <summary>
/// Delivers a notification to the owner of the watchlist that produced it, in that owner's language. Watchlists are
/// per user now, so a single destination chat would hand one person another person's vacancies; a watchlist without an
/// owner (a system one) still goes to <c>Telegram:DefaultChatId</c>.
/// </summary>
public sealed class TelegramSink(
    TelegramClientFacade client,
    MessageFormatter messageFormatter,
    IWatchlistStorage watchlists,
    IBotUserStorage users,
    IOptionsMonitor<DeliveryOptions> deliveryOpts,
    IOptionsMonitor<TelegramOptions> tgOpts,
    ILog log) : IVacancySink
{
    private readonly ILog ctxLog = log.ForContext<TelegramSink>();

    public async Task<DeliveryResult> DeliverAsync(
        IReadOnlyList<OutboxItem> batch,
        CancellationToken ct)
    {
        if (batch.Count == 0)
            return DeliveryResult.Ok;

        var deliveryOptsValue = deliveryOpts.CurrentValue;
        var fallbackChat = tgOpts.CurrentValue.DefaultChatId;

        var pause = TimeSpan.FromSeconds(deliveryOptsValue.DelayBetweenMessagesSeconds);

        var routes = await ResolveRoutesAsync(batch, fallbackChat, ct);

        var first = true;

        foreach (var (target, items) in routes)
        {
            var messages = messageFormatter.Format(items, target.Language);

            foreach (var message in messages)
            {
                // The pause sits between messages, not before the first one of the batch.
                if (!first && pause > TimeSpan.Zero)
                    await Task.Delay(pause, ct);

                first = false;

                var result = await client.SendRichMessageAsync(target.ChatId, message, ct);

                if (result.Success)
                    continue;

                ctxLog.Warn(
                    "Telegram failed to send message to chat {Chat}: {Error}", target.ChatId, result.Error);

                // The whole batch is failed and rescheduled - the outbox has no per-item delivery state.
                return DeliveryResult.Fail(result.Error ?? "unknown", result.RetryAfter);
            }
        }

        return DeliveryResult.Ok;
    }

    /// <summary>
    /// Groups the batch by where it has to go. Watchlists and users are read once per batch, not once per item.
    /// </summary>
    private async Task<IReadOnlyList<(DeliveryTarget Target, List<OutboxItem> Items)>> ResolveRoutesAsync(
        IReadOnlyList<OutboxItem> batch,
        string fallbackChat,
        CancellationToken ct)
    {
        var owners = new Dictionary<long, long?>();

        foreach (var watchlistId in batch.Where(i => i.WatchlistId is not null).Select(i => i.WatchlistId!.Value).Distinct())
            owners[watchlistId] = (await watchlists.GetAsync(watchlistId, ct))?.OwnerUserId;

        var userIds = owners.Values.Where(x => x is not null).Select(x => x!.Value).Distinct().ToList();
        var botUsers = await users.GetManyAsync(userIds, ct);

        var grouped = new Dictionary<DeliveryTarget, List<OutboxItem>>();

        foreach (var item in batch)
        {
            var target = Route(item, owners, botUsers, fallbackChat);

            if (!grouped.TryGetValue(target, out var items))
                grouped[target] = items = [];

            items.Add(item);
        }

        return [.. grouped.Select(x => (x.Key, x.Value))];
    }

    private DeliveryTarget Route(
        OutboxItem item,
        IReadOnlyDictionary<long, long?> owners,
        IReadOnlyDictionary<long, BotUser> botUsers,
        string fallbackChat)
    {
        // A synthetic item (the /show_state dump) has no watchlist, and a system watchlist has no owner.
        if (item.WatchlistId is not { } watchlistId
            || owners.GetValueOrDefault(watchlistId) is not { } ownerId)
        {
            return new DeliveryTarget(fallbackChat, BotLanguage.English);
        }

        if (botUsers.TryGetValue(ownerId, out var owner))
            return new DeliveryTarget(owner.ChatId, owner.Language);

        // The owner has a watchlist but was never seen by the bot - nothing to deliver to but the default chat.
        ctxLog.Warn("Owner {Owner} of watchlist {Watchlist} is unknown — using the default chat", ownerId, watchlistId);

        return new DeliveryTarget(fallbackChat, BotLanguage.English);
    }

    private readonly record struct DeliveryTarget(string ChatId, BotLanguage Language);
}
