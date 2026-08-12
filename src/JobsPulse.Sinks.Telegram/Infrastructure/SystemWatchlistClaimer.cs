using JobsPulse.Core.Pipeline;
using JobsPulse.Sinks.Telegram.Models;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Sinks.Telegram.Infrastructure;

/// <summary>
/// Gives the watchlists that belong to nobody - the legacy import and everything created through the admin commands -
/// to the administrator, the first time they talk to the bot. A migration cannot do this: it does not know the
/// telegram user id of the person, and the bot only learns it from an incoming update.
/// <br/>
/// The claim runs once per process per user: it is one update statement, but it is on the path of every message.
/// </summary>
public sealed class SystemWatchlistClaimer(WatchService watch, ILog log)
{
    private readonly ILog ctxLog = log.ForContext<SystemWatchlistClaimer>();

    private readonly HashSet<long> claimed = [];

    public async Task ClaimAsync(BotContext ctx, CancellationToken ct)
    {
        if (!ctx.IsAdmin)
            return;

        lock (claimed)
        {
            if (!claimed.Add(ctx.UserId))
                return;
        }

        try
        {
            var count = await watch.ClaimSystemWatchlistsAsync(ctx.UserId, ct);
            if (count > 0)
                ctxLog.Info("Claimed {Count} system watchlists for admin {User}", count, ctx.UserId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A failed claim must not swallow the update that triggered it - the next restart tries again.
            lock (claimed)
            {
                claimed.Remove(ctx.UserId);
            }

            ctxLog.Warn(ex, "Claiming the system watchlists for admin {User} has failed", ctx.UserId);
        }
    }
}
