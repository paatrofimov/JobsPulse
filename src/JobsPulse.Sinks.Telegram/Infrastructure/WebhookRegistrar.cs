using JobsPulse.Sinks.Telegram.Options;
using Microsoft.Extensions.Options;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Sinks.Telegram.Infrastructure;

public sealed class WebhookRegistrar(
    TelegramClientFacade client,
    BotMenuPublisher menuPublisher,
    IOptions<TelegramWebhookOptions> options,
    ILog log)
{
    private readonly ILog ctxLog = log.ForContext<WebhookRegistrar>();

    /// <summary>
    /// Points Telegram at the webhook host and publishes the command menu. Runs once per deploy rather than on every
    /// start of a host that scales to zero - a cold start must not pay for two Bot API calls.
    /// </summary>
    public async Task RegisterAsync(CancellationToken ct)
    {
        var opts = options.Value;

        if (string.IsNullOrWhiteSpace(opts.PublicUrl) || string.IsNullOrWhiteSpace(opts.SecretToken))
            throw new InvalidOperationException("TelegramWebhook:PublicUrl and TelegramWebhook:SecretToken are required");

        var url = opts.PublicUrl.TrimEnd('/') + "/" + opts.Path.TrimStart('/');
        var result = await client.SetWebhookAsync(url, opts.SecretToken, ct);

        if (!result.Success)
            throw new InvalidOperationException($"setWebhook has failed: {result.Error}");

        ctxLog.Info("Webhook is set to {Url}", url);

        await menuPublisher.PublishAsync(ct);
    }
}
