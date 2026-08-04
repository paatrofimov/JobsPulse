using System.Collections.Concurrent;
using JobsPulse.Core.Model;

namespace JobsPulse.Sinks.Telegram.Bot;

/// <summary>
/// Состояние диалога «/watch Finom → 1» — пользователь отвечает номером из списка.
/// В памяти намеренно: диалог живёт секунды, переживать рестарт ему незачем.
/// </summary>
public sealed class PendingSelectionStore(TimeProvider clock)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, Pending> _byChat = new(StringComparer.Ordinal);

    public void Set(string chatId, IReadOnlyList<BoardCandidate> candidates) =>
        _byChat[chatId] = new Pending(candidates, clock.GetUtcNow().Add(Lifetime));

    public IReadOnlyList<BoardCandidate>? Take(string chatId)
    {
        if (!_byChat.TryRemove(chatId, out var pending)) return null;
        return pending.ExpiresAt < clock.GetUtcNow() ? null : pending.Candidates;
    }

    public void Clear(string chatId) => _byChat.TryRemove(chatId, out _);

    private sealed record Pending(IReadOnlyList<BoardCandidate> Candidates, DateTimeOffset ExpiresAt);
}
