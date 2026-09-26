using JobsPulse.Core.Model.Infrastructure;

namespace JobsPulse.Core.Abstractions;

/// <summary>
/// Postings rejected by the storage filters after their detail was read (`rejected_posting`). The counterpart of
/// `seen_vacancy` for what was *not* stored: without it a posting failing a description filter is unknown on every
/// poll and its detail is requested again, hour after hour.
/// </summary>
public interface IRejectedPostingStorage
{
    /// <summary>Every rejected posting of the board, whatever filter set rejected it, by post id.</summary>
    Task<IReadOnlyDictionary<string, RejectedPosting>> LoadAsync(string sourceId, string boardId, CancellationToken ct);

    /// <summary>Writes the difference to the stored set in one transaction; nothing to write costs no round trip.</summary>
    Task SaveAsync(
        string sourceId,
        string boardId,
        IReadOnlyList<RejectedPosting> upserts,
        IReadOnlyList<string> removals,
        CancellationToken ct);
}
