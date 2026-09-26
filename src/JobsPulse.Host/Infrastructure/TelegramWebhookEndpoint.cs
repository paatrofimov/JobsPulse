using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JobsPulse.Core.Abstractions;
using JobsPulse.Sinks.Telegram.Options;
using JobsPulse.Sinks.Telegram.Pipeline;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Host.Infrastructure;

public static class TelegramWebhookEndpoint
{
    public const string SecretHeader = "X-Telegram-Bot-Api-Secret-Token";

    public static void Map(WebApplication app)
    {
        var opts = app.Services.GetRequiredService<IOptions<TelegramWebhookOptions>>().Value;

        if (string.IsNullOrWhiteSpace(opts.SecretToken))
            throw new InvalidOperationException("TelegramWebhook:SecretToken is required for the webhook role");

        app.MapGet("/healthz", () => Results.Text("ok"));
        app.MapPost(opts.Path, HandleAsync);
    }

    /// <summary>
    /// One update per request, handled before the response: Cloud Run gives the container CPU only while a request
    /// is open. A failing update still answers 200 - Telegram would otherwise redeliver it over and over, the same
    /// reason the long-polling listener moves its offset past a broken update.
    /// </summary>
    private static async Task<IResult> HandleAsync(
        HttpRequest request,
        BotUpdateHandler handler,
        IPollingTrigger trigger,
        IOptions<TelegramWebhookOptions> options,
        ILog log)
    {
        var ctxLog = log.ForContext(typeof(TelegramWebhookEndpoint));
        var ct = request.HttpContext.RequestAborted;

        if (!IsAuthentic(request, options.Value.SecretToken!))
            return Results.Unauthorized();

        Update? update;
        try
        {
            update = await JsonSerializer.DeserializeAsync<Update>(request.Body, JsonBotAPI.Options, ct);
        }
        catch (JsonException ex)
        {
            ctxLog.Warn(ex, "Unreadable webhook payload");
            return Results.BadRequest();
        }

        if (update is null)
            return Results.BadRequest();

        try
        {
            await handler.HandleAsync(update, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ctxLog.Error(ex, "Update {Update} has failed", update.Id);
        }

        // A workflow dispatch the update started must leave before the CPU is throttled.
        if (trigger is GitHubWorkflowTrigger remote)
            await remote.PendingAsync();

        return Results.Ok();
    }

    private static bool IsAuthentic(HttpRequest request, string secret)
    {
        var received = request.Headers[SecretHeader].ToString();

        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(received), Encoding.UTF8.GetBytes(secret));
    }
}
