using JobsPulse.Core.Model.Infrastructure;

namespace JobsPulse.Core.Pipeline;

public sealed record LookupResult
{
    public required LookupStatus Status { get; init; }
    public required string Query { get; init; }
    public IReadOnlyList<BoardCandidate> Candidates { get; init; } = [];
    public WatchlistEntry? Existing { get; init; }

    public static LookupResult Found(string query, IReadOnlyList<BoardCandidate> candidates) =>
        new() { Status = LookupStatus.Found, Query = query, Candidates = candidates };

    public static LookupResult NotFound(string query) =>
        new() { Status = LookupStatus.NotFound, Query = query };

    public static LookupResult AlreadyWatched(WatchlistEntry entry) =>
        new() { Status = LookupStatus.AlreadyWatched, Query = entry.CompanyName, Existing = entry };
}

public enum LookupStatus
{
    Found,
    NotFound,
    AlreadyWatched
}
