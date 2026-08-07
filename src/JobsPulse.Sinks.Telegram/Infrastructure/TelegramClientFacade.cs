using JobsPulse.Sinks.Telegram.Models;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace JobsPulse.Sinks.Telegram.Infrastructure;

public sealed class TelegramClientFacade(TelegramBotClient client)
{
    public async Task<TelegramResult> SendMessageAsync(
        string chatId,
        string html,
        CancellationToken ct)
    {
        try
        {
            await client.SendMessage(
                chatId: chatId,
                text: html,
                parseMode: ParseMode.Html,
                disableNotification: false,
                linkPreviewOptions: new LinkPreviewOptions
                {
                    IsDisabled = true
                },
                cancellationToken: ct);

            return TelegramResult.Ok;
        }
        catch (ApiRequestException ex)
        {
            var retryAfter = ex.Parameters?.RetryAfter is { } seconds
                ? TimeSpan.FromSeconds(seconds)
                : (TimeSpan?)null;

            return TelegramResult.Fail(ex.Message, retryAfter);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return TelegramResult.Fail(ex.Message);
        }
    }

    public async Task<IReadOnlyList<Update>> GetUpdatesAsync(
        int offset,
        int timeoutSeconds,
        CancellationToken ct)
    {
        try
        {
            return await client.GetUpdates(
                offset: offset,
                timeout: timeoutSeconds,
                allowedUpdates: [UpdateType.Message],
                cancellationToken: ct);
        }
        catch (ApiRequestException)
        {
            return [];
        }
    }
}