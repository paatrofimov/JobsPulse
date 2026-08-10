using System.Collections.Concurrent;
using JobsPulse.Core.Model.Infrastructure;

namespace JobsPulse.Sinks.Telegram.Infrastructure;

public sealed class PendingSelectionStore(TimeProvider clock)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, Pending> _byChat = new(StringComparer.Ordinal);

    /// <summary>The target watchlist is part of the dialogue state - the answer «1» carries no destination.</summary>
    public void Set(string chatId, long watchlistId, IReadOnlyList<BoardCandidate> candidates) =>
        _byChat[chatId] = new Pending(watchlistId, candidates, clock.GetUtcNow().Add(Lifetime));

    public Pending? Take(string chatId)
    {
        if (!_byChat.TryRemove(chatId, out var pending)) return null;
        return pending.ExpiresAt < clock.GetUtcNow() ? null : pending;
    }

    public void Clear(string chatId) => _byChat.TryRemove(chatId, out _);

    public sealed record Pending(long WatchlistId, IReadOnlyList<BoardCandidate> Candidates, DateTimeOffset ExpiresAt);
}
