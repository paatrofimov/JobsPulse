using JobsPulse.Sinks.Telegram.Infrastructure;
using JobsPulse.Sinks.Telegram.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot.Types;

namespace JobsPulse.Sinks.Telegram.Routines;

public sealed class TelegramBotListener(
    TelegramClientFacade client,
    CommandRouter router,
    IOptions<TelegramOptions> options,
    ILogger<TelegramBotListener> log) : BackgroundService
{
    private const int LongPollSeconds = 30;

    private int _offset;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;

        if (!opts.EnableCommands)
        {
            log.LogInformation(
                "Bot commands are disabled (EnableCommands=false)");
            return;
        }

        if (opts.AdminChatIds.Count == 0)
        {
            log.LogWarning(
                "AdminChatIds list is empty — commands will not be applied");
        }

        log.LogInformation("Listening commands");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var updates = await client.GetUpdatesAsync(
                    _offset,
                    LongPollSeconds,
                    stoppingToken);

                foreach (var update in updates)
                {
                    _offset = update.Id + 1;

                    await HandleAsync(
                        update,
                        opts,
                        stoppingToken);
                }
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error in Telegram getUpdates loop");

                await Task.Delay(
                    TimeSpan.FromSeconds(10),
                    stoppingToken);
            }
        }
    }

    private async Task HandleAsync(
        Update update,
        TelegramOptions opts,
        CancellationToken ct)
    {
        var message = update.Message;

        if (string.IsNullOrWhiteSpace(message?.Text))
            return;

        var chatId = message.Chat.Id.ToString();

        if (!opts.AdminChatIds.Contains(
                chatId,
                StringComparer.Ordinal))
        {
            log.LogWarning(
                "Command from unauthorized chat {ChatId} ignored",
                chatId);

            return;
        }

        string reply;

        try
        {
            reply = await router.HandleAsync(
                chatId,
                message.Text,
                ct);
        }
        catch (Exception ex)
            when (ex is not OperationCanceledException)
        {
            log.LogError(
                ex,
                "Command '{Text}' failed",
                message.Text);

            reply = "Something wrong, see logs";
        }

        await client.SendRichMessageAsync(
            chatId,
            new InputRichMessage() {Html = reply},
            ct);
    }
}