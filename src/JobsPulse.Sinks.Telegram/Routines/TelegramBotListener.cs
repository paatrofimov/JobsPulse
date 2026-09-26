using JobsPulse.Sinks.Telegram.Infrastructure;
using JobsPulse.Sinks.Telegram.Options;
using JobsPulse.Sinks.Telegram.Pipeline;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Telegram.Bot.Types;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Sinks.Telegram.Routines;

public sealed class TelegramBotListener(
    TelegramClientFacade client,
    BotUpdateHandler handler,
    BotMenuPublisher menuPublisher,
    IOptions<TelegramOptions> options,
    ILog log) : BackgroundService
{
    private const int LongPollSeconds = 30;

    private readonly ILog ctxLog = log.ForContext<TelegramBotListener>();

    private int offset;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;

        if (!opts.EnableCommands)
        {
            ctxLog.Info("Bot commands are disabled (EnableCommands=false)");
            return;
        }

        if (opts.AdminChatIds.Count == 0)
            ctxLog.Warn("AdminChatIds is empty — the admin section is unreachable");

        ctxLog.Info(
            opts.AllowedUserIds.Count == 0
                ? "Bot is open to every telegram user — watchlists are owned per user"
                : "Bot is restricted to {Count} allowed users",
            opts.AllowedUserIds.Count);

        // Telegram refuses getUpdates while a webhook is set. Taking over by deleting it would silently cut off the
        // deployed webhook host - e.g. a local run with the production token - so long polling steps aside instead.
        var webhook = await client.GetWebhookUrlAsync(stoppingToken);
        if (!string.IsNullOrEmpty(webhook))
        {
            ctxLog.Warn(
                "A webhook is registered ({Url}) — long polling is not started. Updates go to the webhook host; "
                + "call deleteWebhook to switch this bot back to long polling",
                webhook);
            return;
        }

        await menuPublisher.PublishAsync(stoppingToken);

        ctxLog.Info("Listening for updates");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var updates = await client.GetUpdatesAsync(offset, LongPollSeconds, stoppingToken);

                foreach (var update in updates)
                {
                    offset = update.Id + 1;

                    await HandleSafeAsync(update, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                ctxLog.Error(ex, "Error in Telegram getUpdates loop");

                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }

    /// <summary>
    /// One broken update must not stop the loop or replay forever: the offset has already moved on, so a failure is
    /// logged and the next update is taken.
    /// </summary>
    private async Task HandleSafeAsync(Update update, CancellationToken ct)
    {
        try
        {
            await handler.HandleAsync(update, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ctxLog.Error(ex, "Update {Update} has failed", update.Id);
        }
    }
}
