using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobsPulse.Sinks.Telegram;

/// <summary>
/// Приёмник уведомлений. Отвечает только за «как показать и отправить»;
/// когда отправлять и что делать с неудачей — забота диспетчера outbox.
/// </summary>
public sealed class TelegramSink(
    TelegramClient client,
    IOptionsMonitor<DeliveryOptions> delivery,
    ILogger<TelegramSink> log) : IVacancySink
{
    public string SinkId => "telegram";

    public async Task<DeliveryResult> DeliverAsync(
        string chatId, IReadOnlyList<OutboxItem> batch, CancellationToken ct)
    {
        if (batch.Count == 0) return DeliveryResult.Ok;

        var opts = delivery.CurrentValue;
        var pause = TimeSpan.FromSeconds(opts.DelayBetweenMessagesSeconds);
        var silent = batch.All(i => i.Silent);

        var messages = MessageFormatter.Format(batch);

        for (var i = 0; i < messages.Count; i++)
        {
            var result = await client.SendMessageAsync(chatId, messages[i], silent, ct);

            if (!result.Success)
            {
                log.LogWarning("Telegram отклонил сообщение в {Chat}: {Error}", chatId, result.Error);
                return DeliveryResult.Fail(result.Error ?? "unknown", result.RetryAfter);
            }

            // Пауза между сообщениями: Telegram душит примерно на 20 сообщениях в минуту в один групповой чат.
            if (i < messages.Count - 1 && pause > TimeSpan.Zero)
                await Task.Delay(pause, ct);
        }

        return DeliveryResult.Ok;
    }
}
