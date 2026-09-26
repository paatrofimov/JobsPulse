using Vostok.Logging.Abstractions;

namespace JobsPulse.Sinks.Telegram.Infrastructure;

public sealed class BotMenuPublisher(TelegramClientFacade client, ILog log)
{
    private readonly ILog ctxLog = log.ForContext<BotMenuPublisher>();

    /// <summary>The client menu is published per language, so a Russian client shows Russian descriptions.</summary>
    public async Task PublishAsync(CancellationToken ct)
    {
        foreach (var (_, code, commands) in BotCommandCatalog.All())
        {
            var menu = await client.SetCommandsAsync(commands, ct, code);
            if (!menu.Success)
                ctxLog.Warn("Failed to publish the '{Code}' command menu: {Error}", code, menu.Error);
        }
    }
}
