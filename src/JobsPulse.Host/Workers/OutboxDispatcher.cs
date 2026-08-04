using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Options;
using JobsPulse.Storage;
using Microsoft.Extensions.Options;

namespace JobsPulse.Host.Workers;

/// <summary>
/// Отправка уведомлений из outbox.
///
/// Живёт отдельно от поллинга специально: если Telegram лёг или упёрлись в его лимиты,
/// вакансии всё равно детектируются и копятся в очереди, а не теряются.
/// Ретраи — экспоненциальный backoff, после MaxAttempts элемент уходит в dead-letter.
/// </summary>
public sealed class OutboxDispatcher(
    IOutbox outbox,
    SqliteOutbox maintenance,
    IVacancySink sink,
    IOptionsMonitor<DeliveryOptions> options,
    ILogger<OutboxDispatcher> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var opts = options.CurrentValue;

            try
            {
                await maintenance.DeadLetterAsync(opts.MaxAttempts, stoppingToken);

                var batch = await outbox.LeaseAsync(opts.BatchSize, stoppingToken);

                if (batch.Count > 0)
                {
                    // Группировка по чату: лимиты Telegram считаются на чат, и формат сообщения тоже.
                    foreach (var group in batch.GroupBy(i => i.ChatId, StringComparer.Ordinal))
                        await DeliverGroupAsync(group.Key, group.ToList(), opts, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Диспетчер outbox упал на итерации");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(opts.DispatchIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task DeliverGroupAsync(
        string chatId, IReadOnlyList<OutboxItem> items, DeliveryOptions opts, CancellationToken ct)
    {
        var ids = items.Select(i => i.Id).ToList();
        var result = await sink.DeliverAsync(chatId, items, ct);

        if (result.Success)
        {
            await outbox.MarkSentAsync(ids, ct);
            log.LogInformation("Отправлено {Count} уведомлений в {Chat}", items.Count, chatId);
            return;
        }

        // Если приёмник назвал свой срок ожидания — берём его; иначе экспоненциальный backoff.
        var attempts = items.Max(i => i.Attempts);
        var backoff = result.RetryAfter ?? TimeSpan.FromSeconds(Math.Min(300, Math.Pow(2, attempts + 1)));

        await outbox.MarkFailedAsync(ids, backoff, result.Error ?? "unknown", ct);
        log.LogWarning("Доставка в {Chat} не удалась ({Error}), повтор через {Backoff}",
            chatId, result.Error, backoff);
    }
}
