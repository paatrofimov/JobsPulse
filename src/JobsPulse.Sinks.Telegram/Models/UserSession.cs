using JobsPulse.Core.Model.Infrastructure;

namespace JobsPulse.Sinks.Telegram.Models;

/// <summary>
/// The short-lived dialogue state of one user: what free text is awaited and for which watchlist, plus the board
/// candidates of an «add company» search - the answer «this one» carries no destination of its own.
/// </summary>
public sealed record UserSession
{
    public PendingInputKind Pending { get; init; } = PendingInputKind.None;

    public long WatchlistId { get; init; }

    public IReadOnlyList<BoardCandidate> Candidates { get; init; } = [];

    public required DateTimeOffset ExpiresAt { get; init; }
}
