using JobsPulse.Core.Model.Infrastructure;

namespace JobsPulse.Core.Abstractions;

/// <summary>
/// When each board was last traversed (`board_poll_state`) - the scheduling state of both polling cycles.
///
/// It used to be a dictionary in each cycle, which meant a restart re-read every board of every watchlist at once
/// and sent the registry sweep back to its first board - the longest procedure in the system, repeated from zero on
/// every deploy. It also meant the progress an operator reads started at «0 of 18088» every time. Both are the same
/// missing row.
/// </summary>
public interface IBoardPollStateStorage
{
    /// <summary>Every board ever traversed, keyed <c>{sourceId}/{boardId}</c>, case-insensitive.</summary>
    Task<IReadOnlyDictionary<string, DateTimeOffset>> LoadAsync(CancellationToken ct);

    /// <summary>
    /// Records a finished traversal of these boards - one idempotent upsert for the whole slice. A board that
    /// failed is stamped too: leaving it unstamped would make it the least-recently-polled board forever and the
    /// walk would never get past it.
    /// </summary>
    Task StampAsync(IReadOnlyList<BoardPollStamp> stamps, CancellationToken ct);
}
