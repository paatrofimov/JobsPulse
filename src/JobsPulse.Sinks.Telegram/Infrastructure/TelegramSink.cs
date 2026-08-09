using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model.Domain;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Core.Options;
using JobsPulse.Sinks.Telegram.Options;
using Microsoft.Extensions.Options;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Sinks.Telegram.Infrastructure;

public sealed class TelegramSink(
    TelegramClientFacade client,
    MessageFormatter messageFormatter,
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
        var tgOptsValue = tgOpts.CurrentValue;

        var chatId = tgOptsValue.DefaultChatId;

        var pause = TimeSpan.FromSeconds(deliveryOptsValue.DelayBetweenMessagesSeconds);

        var messages = messageFormatter.Format(batch);

        for (var i = 0; i < messages.Count; i++)
        {
            var result = await client.SendRichMessageAsync(chatId, messages[i], ct);

            if (!result.Success)
            {
                ctxLog.Warn("Telegram failed to send message to chat {Chat}: {Error}", chatId, result.Error);
                return DeliveryResult.Fail(result.Error ?? "unknown", result.RetryAfter);
            }

            if (i < messages.Count - 1 && pause > TimeSpan.Zero)
                await Task.Delay(pause, ct);
        }

        return DeliveryResult.Ok;
    }
}