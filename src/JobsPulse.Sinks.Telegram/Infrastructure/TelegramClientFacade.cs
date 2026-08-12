using JobsPulse.Sinks.Telegram.Models;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace JobsPulse.Sinks.Telegram.Infrastructure;

public sealed class TelegramClientFacade(ITelegramBotClient client)
{
    public async Task<TelegramResult> SendRichMessageAsync(
        string chatId,
        InputRichMessage msg,
        CancellationToken ct,
        ReplyMarkup? keyboard = null)
    {
        try
        {
            await client.SendRichMessage(
                chatId: chatId,
                msg,
                replyMarkup: keyboard,
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

    /// <summary>
    /// Replaces the message a button was pressed on, so the bot is one screen the user navigates instead of a
    /// growing wall of replies.
    /// </summary>
    public async Task<TelegramResult> EditRichMessageAsync(
        string chatId,
        int messageId,
        InputRichMessage msg,
        InlineKeyboardMarkup? keyboard,
        CancellationToken ct)
    {
        try
        {
            await client.EditMessageText(
                chatId: chatId,
                messageId: messageId,
                text: null!,
                replyMarkup: keyboard,
                richMessage: msg,
                cancellationToken: ct);

            return TelegramResult.Ok;
        }
        catch (ApiRequestException ex)
        {
            // Tapping the same button twice edits a message into itself, which telegram rejects. Not an error.
            if (ex.Message.Contains("message is not modified", StringComparison.OrdinalIgnoreCase))
                return TelegramResult.Ok;

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

    /// <summary>
    /// Stops the button's spinner, optionally with a toast. Telegram wants this for every callback query, so a
    /// failure here is swallowed - it must never break the screen that was just rendered.
    /// </summary>
    public async Task AnswerCallbackAsync(string callbackQueryId, string? toast, CancellationToken ct)
    {
        try
        {
            await client.AnswerCallbackQuery(callbackQueryId, toast, cancellationToken: ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _ = ex;
        }
    }

    /// <summary>Populates the bot menu, so commands are suggested by the Telegram client itself.</summary>
    public async Task<TelegramResult> SetCommandsAsync(
        IEnumerable<BotCommand> commands,
        CancellationToken ct,
        string? languageCode = null)
    {
        try
        {
            await client.SetMyCommands(commands, languageCode: languageCode, cancellationToken: ct);
            return TelegramResult.Ok;
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
                allowedUpdates: [UpdateType.Message, UpdateType.CallbackQuery],
                cancellationToken: ct);
        }
        catch (ApiRequestException)
        {
            return [];
        }
    }
}
