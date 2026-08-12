using JobsPulse.Core.Abstractions;
using JobsPulse.Sinks.Telegram.Infrastructure;
using JobsPulse.Sinks.Telegram.Infrastructure.Localization;
using JobsPulse.Sinks.Telegram.Models;
using JobsPulse.Sinks.Telegram.Options;
using Microsoft.Extensions.Options;
using Telegram.Bot.Types;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Sinks.Telegram.Pipeline;

/// <summary>
/// One entry point for everything arriving from telegram. It resolves who is talking, decides whether the update is a
/// button, an awaited answer, a user command or an admin command, and sends the resulting screen - editing the
/// existing message for a button press, so the bot stays one screen instead of a growing wall of replies.
/// </summary>
public sealed class BotUpdateHandler(
    IBotUserStorage users,
    UserSessionStore sessions,
    ScreenRouter screens,
    CommandRouter adminCommands,
    SystemWatchlistClaimer claimer,
    TelegramClientFacade client,
    IOptionsMonitor<TelegramOptions> options,
    ILog log)
{
    private readonly ILog ctxLog = log.ForContext<BotUpdateHandler>();

    public async Task HandleAsync(Update update, CancellationToken ct)
    {
        if (update.CallbackQuery is { } callback)
        {
            await HandleCallbackAsync(callback, ct);
            return;
        }

        if (update.Message is { } message)
            await HandleMessageAsync(message, ct);
    }

    private async Task HandleMessageAsync(Message message, CancellationToken ct)
    {
        if (message.From is not { } from || string.IsNullOrWhiteSpace(message.Text))
            return;

        var ctx = await ResolveAsync(from, message.Chat.Id.ToString(), ct);
        if (ctx is null)
            return;

        var text = message.Text.Trim();

        // An armed step owns any plain text - the user is answering a question the bot just asked. A session that
        // only holds candidate buttons is left alone: it is waiting for a tap, not for text.
        if (!text.StartsWith('/')
            && sessions.Peek(ctx.UserId) is { Pending: not PendingInputKind.None } session)
        {
            sessions.Clear(ctx.UserId);

            if (await screens.HandleTextAsync(ctx, session, text, ct) is { } answer)
            {
                await SendAsync(ctx, answer, ct);
                return;
            }
        }

        await HandleCommandAsync(ctx, text, ct);
    }

    private async Task HandleCommandAsync(BotContext ctx, string text, CancellationToken ct)
    {
        var command = ParseCommand(text);

        switch (command)
        {
            case BotCommandCatalog.Start or BotCommandCatalog.Menu or "":
                sessions.Clear(ctx.UserId);
                await SendAsync(ctx, (await screens.RenderAsync(ctx, new CallbackData(CallbackAction.Menu), ct)).View, ct);
                return;

            case BotCommandCatalog.Help:
                await SendAsync(ctx, (await screens.RenderAsync(ctx, new CallbackData(CallbackAction.Help), ct)).View, ct);
                return;

            case BotCommandCatalog.Language:
                await SendAsync(
                    ctx, (await screens.RenderAsync(ctx, new CallbackData(CallbackAction.Language), ct)).View, ct);
                return;
        }

        if (AdminCommandCatalog.IsAdminCommand(command))
        {
            if (!ctx.IsAdmin)
            {
                ctxLog.Warn("Admin command '{Command}' from non-admin chat {Chat} refused", command, ctx.ChatId);

                await SendAsync(
                    ctx,
                    new ScreenView(
                        $"<p>{BotTexts.Get(TextKey.AdminOnly, ctx.Language)}</p>",
                        new KeyboardBuilder(ctx.Language).Build(CallbackAction.Menu)),
                    ct);

                return;
            }

            if (command == AdminCommandCatalog.Admin)
            {
                await SendAsync(
                    ctx, (await screens.RenderAsync(ctx, new CallbackData(CallbackAction.Admin), ct)).View, ct);

                return;
            }

            var reply = await adminCommands.HandleAsync(ctx.UserId, ctx.ChatId, text, ct);

            await SendAsync(ctx, new ScreenView(reply), ct);
            return;
        }

        await SendAsync(ctx, screens.UnknownCommand(ctx), ct);
    }

    private async Task HandleCallbackAsync(CallbackQuery callback, CancellationToken ct)
    {
        if (callback.From is not { } from)
            return;

        var chatId = callback.Message?.Chat.Id.ToString();
        if (chatId is null)
        {
            await client.AnswerCallbackAsync(callback.Id, null, ct);
            return;
        }

        var ctx = await ResolveAsync(from, chatId, ct);
        if (ctx is null)
        {
            await client.AnswerCallbackAsync(callback.Id, null, ct);
            return;
        }

        var data = CallbackData.Parse(callback.Data);

        // Leaving a screen abandons whatever text it was waiting for; the picker keeps its candidate list.
        if (data.Action is not (CallbackAction.CompanyPick or CallbackAction.None))
            sessions.Clear(ctx.UserId);

        var (view, updated) = await screens.RenderAsync(ctx, data, ct);

        // The spinner is stopped first: the edit below may take a moment, and a stuck button looks broken.
        await client.AnswerCallbackAsync(callback.Id, view.Toast, ct);

        var edited = await client.EditRichMessageAsync(
            updated.ChatId,
            callback.Message!.MessageId,
            new InputRichMessage { Html = view.Html },
            view.Keyboard,
            ct);

        // An un-editable message (too old, or sent by somebody else) still has to produce the screen.
        if (!edited.Success)
        {
            ctxLog.Debug("Screen edit failed ({Error}) — sending a new message instead", edited.Error);
            await SendAsync(updated, view, ct);
        }
    }

    /// <summary>Null means the user is not allowed to talk to this bot at all.</summary>
    private async Task<BotContext?> ResolveAsync(User from, string chatId, CancellationToken ct)
    {
        var opts = options.CurrentValue;

        if (opts.AllowedUserIds.Count > 0 && !opts.AllowedUserIds.Contains(from.Id))
        {
            ctxLog.Warn("Update from user {User} ignored — not in Telegram:AllowedUserIds", from.Id);
            return null;
        }

        var user = await users.UpsertOnContactAsync(
            from.Id,
            chatId,
            DisplayName(from),
            BotTexts.FromTelegramCode(from.LanguageCode),
            ct);

        var ctx = new BotContext
        {
            User = user,
            ChatId = chatId,
            IsAdmin = opts.IsAdmin(from.Username, chatId)
        };

        await claimer.ClaimAsync(ctx, ct);

        return ctx;
    }

    private async Task SendAsync(BotContext ctx, ScreenView view, CancellationToken ct)
    {
        var result = await client.SendRichMessageAsync(
            ctx.ChatId, new InputRichMessage { Html = view.Html }, ct, view.Keyboard);

        if (!result.Success)
            ctxLog.Warn("Failed to send a screen to chat {Chat}: {Error}", ctx.ChatId, result.Error);
    }

    private static string? DisplayName(User from) =>
        from.Username is { Length: > 0 } username
            ? $"@{username}"
            : string.Join(' ', new[] { from.FirstName, from.LastName }.Where(x => !string.IsNullOrWhiteSpace(x)));

    /// <summary>`/watch@my_bot arg` to `watch`. A plain message has no command and reads as empty.</summary>
    private static string ParseCommand(string text)
    {
        if (!text.StartsWith('/'))
            return string.Empty;

        var space = text.IndexOf(' ');
        var command = (space < 0 ? text : text[..space]).TrimStart('/').ToLowerInvariant();

        var at = command.IndexOf('@');

        return at > 0 ? command[..at] : command;
    }
}
