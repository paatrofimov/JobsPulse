using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Core.Pipeline;
using JobsPulse.Sinks.Telegram.Models;

namespace JobsPulse.Sinks.Telegram.Infrastructure;

/// <summary>
/// The single place that decides whether a user may change a watchlist. Every screen resolves through here, so
/// ownership cannot be forgotten in one branch: a watchlist is editable by its owner, a system one (no owner, from
/// the legacy import) only by an admin, and everything else is a read-only example.
/// </summary>
public sealed class WatchlistAccess(WatchService watch)
{
    public async Task<WatchlistAccessResult> ResolveAsync(BotContext ctx, long watchlistId, CancellationToken ct)
    {
        var watchlist = await watch.ResolveAsync(watchlistId.ToString(), ct);

        return watchlist is null
            ? WatchlistAccessResult.Gone
            : new WatchlistAccessResult(watchlist, CanEdit(ctx, watchlist));
    }

    /// <summary>Resolves a company entry of the user together with the watchlist that holds it.</summary>
    public async Task<EntryAccessResult> ResolveEntryAsync(BotContext ctx, long entryId, CancellationToken ct)
    {
        foreach (var watchlist in await watch.ListAsync(ct))
        {
            var entry = watchlist.Entries.FirstOrDefault(e => e.Id == entryId);
            if (entry is null)
                continue;

            return new EntryAccessResult(watchlist, entry, CanEdit(ctx, watchlist));
        }

        return EntryAccessResult.Gone;
    }

    public static bool CanEdit(BotContext ctx, Watchlist watchlist) =>
        watchlist.OwnerUserId == ctx.UserId || (watchlist.OwnerUserId is null && ctx.IsAdmin);

    public static bool IsOwn(BotContext ctx, Watchlist watchlist) => watchlist.OwnerUserId == ctx.UserId;
}

/// <param name="Watchlist">Null only when the watchlist is gone.</param>
public readonly record struct WatchlistAccessResult(Watchlist? Watchlist, bool CanEdit)
{
    public static readonly WatchlistAccessResult Gone = new(null, false);

    public bool Found => Watchlist is not null;
}

public readonly record struct EntryAccessResult(Watchlist? Watchlist, WatchlistEntry? Entry, bool CanEdit)
{
    public static readonly EntryAccessResult Gone = new(null, null, false);

    public bool Found => Entry is not null;
}
