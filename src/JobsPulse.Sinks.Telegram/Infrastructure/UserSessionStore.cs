using System.Collections.Concurrent;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Sinks.Telegram.Models;

namespace JobsPulse.Sinks.Telegram.Infrastructure;

/// <summary>
/// Dialogue state per user, in memory: a step lasts seconds, and losing it on a restart only costs one tap back to
/// the menu. Keyed by the telegram user id, so the same person is one dialogue in any chat.
/// </summary>
public sealed class UserSessionStore(TimeProvider clock)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<long, UserSession> byUser = new();

    /// <summary>Starts waiting for free text of a given kind, for a given watchlist.</summary>
    public void Await(long userId, PendingInputKind kind, long watchlistId) =>
        byUser[userId] = new UserSession
        {
            Pending = kind,
            WatchlistId = watchlistId,
            ExpiresAt = clock.GetUtcNow().Add(Lifetime)
        };

    public void SetCandidates(long userId, long watchlistId, IReadOnlyList<BoardCandidate> candidates) =>
        byUser[userId] = new UserSession
        {
            Pending = PendingInputKind.None,
            WatchlistId = watchlistId,
            Candidates = candidates,
            ExpiresAt = clock.GetUtcNow().Add(Lifetime)
        };

    /// <summary>Reads the session without consuming it. An expired one is dropped and reads as absent.</summary>
    public UserSession? Peek(long userId)
    {
        if (!byUser.TryGetValue(userId, out var session))
            return null;

        if (session.ExpiresAt >= clock.GetUtcNow())
            return session;

        byUser.TryRemove(userId, out _);
        return null;
    }

    public UserSession? Take(long userId)
    {
        var session = Peek(userId);
        if (session is not null)
            byUser.TryRemove(userId, out _);

        return session;
    }

    public void Clear(long userId) => byUser.TryRemove(userId, out _);
}
