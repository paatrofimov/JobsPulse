using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Options;
using JobsPulse.Core.Pipeline;
using Microsoft.Extensions.Options;

namespace JobsPulse.Host.Workers;

/// <summary>
/// Планировщик циклов. Всё содержательное — в <see cref="PollingOrchestrator"/>;
/// здесь только «когда запускать».
///
/// Два момента, которые легко сделать неправильно:
///  • интервал читается ВНУТРИ цикла — иначе смена настройки не подхватится без рестарта;
///  • циклы не накладываются: если обход занял дольше интервала, следующий не стартует параллельно.
/// </summary>
public sealed class PollingWorker(
    PollingOrchestrator orchestrator,
    IStateStore state,
    IOptionsMonitor<PollingOptions> options,
    ILogger<PollingWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await state.InitializeAsync(stoppingToken);

        if (options.CurrentValue.DryRun)
            log.LogWarning("DRY-RUN: уведомления не отправляются, только логируются");

        while (!stoppingToken.IsCancellationRequested)
        {
            var period = TimeSpan.FromMinutes(Math.Max(1, options.CurrentValue.IntervalMinutes));

            try
            {
                await orchestrator.RunCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Цикл поллинга не должен умирать целиком из-за одной ошибки.
                log.LogError(ex, "Цикл поллинга завершился с ошибкой");
            }

            try
            {
                await Task.Delay(period, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
