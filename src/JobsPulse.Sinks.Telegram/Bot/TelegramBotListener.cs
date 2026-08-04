using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobsPulse.Sinks.Telegram.Bot;

/// <summary>
/// Long polling getUpdates. Отдельный BackgroundService: команды бота и цикл поллинга вакансий
/// не должны мешать друг другу.
/// </summary>
public sealed class TelegramBotListener(
    TelegramClient client,
    CommandRouter router,
    IOptions<TelegramOptions> options,
    ILogger<TelegramBotListener> log) : BackgroundService
{
    private const int LongPollSeconds = 30;

    private long _offset;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;

        if (!opts.IsConfigured || !opts.EnableCommands)
        {
            log.LogInformation("Команды бота отключены (нет токена или EnableCommands=false)");
            return;
        }

        if (opts.AdminChatIds.Count == 0)
            log.LogWarning("AdminChatIds пуст — команды не будут приниматься ни от кого. Это защита от чужих правок watchlist");

        log.LogInformation("Слушаю команды бота");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var updates = await client.GetUpdatesAsync(_offset, LongPollSeconds, stoppingToken);

                foreach (var update in updates)
                {
                    _offset = update.UpdateId + 1;
                    await HandleAsync(update, opts, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Ошибка в цикле getUpdates");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }

    private async Task HandleAsync(TelegramUpdate update, TelegramOptions opts, CancellationToken ct)
    {
        var text = update.Message?.Text;
        var chatId = update.Message?.Chat?.Id.ToString();

        if (string.IsNullOrWhiteSpace(text) || chatId is null) return;

        // Бот доступен всем, кто его найдёт. Без белого списка любой сможет менять watchlist.
        if (!opts.AdminChatIds.Contains(chatId, StringComparer.Ordinal))
        {
            log.LogWarning("Команда от неразрешённого чата {Chat} проигнорирована", chatId);
            return;
        }

        string reply;
        try
        {
            reply = await router.HandleAsync(chatId, text, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "Команда '{Text}' упала", text);
            reply = "Что-то пошло не так, подробности в логах.";
        }

        await client.SendMessageAsync(chatId, reply, silent: true, ct);
    }
}
