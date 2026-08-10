using JobsPulse.Core.Model.Infrastructure;

namespace JobsPulse.Core.Pipeline;

public sealed record BoardAddResult
{
    public required BoardAddStatus Status { get; init; }

    public WatchlistEntry? Entry { get; init; }

    /// <summary>Source id or board id the failure is about.</summary>
    public string? Subject { get; init; }

    public static BoardAddResult Ok(WatchlistEntry entry) =>
        new() { Status = BoardAddStatus.Added, Entry = entry };

    public static BoardAddResult UnknownSource(string sourceId) =>
        new() { Status = BoardAddStatus.UnknownSource, Subject = sourceId };

    public static BoardAddResult BoardNotFound(string boardId) =>
        new() { Status = BoardAddStatus.BoardNotFound, Subject = boardId };

    public static BoardAddResult WatchlistNotFound() =>
        new() { Status = BoardAddStatus.WatchlistNotFound };
}

public enum BoardAddStatus
{
    Added,
    UnknownSource,
    BoardNotFound,
    WatchlistNotFound
}
