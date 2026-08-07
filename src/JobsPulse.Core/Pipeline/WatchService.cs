using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model.Infrastructure;
using Microsoft.Extensions.Logging;

namespace JobsPulse.Core.Pipeline;

public sealed class WatchService(
    IWatchlistProvider watchlist,
    ISourceCatalog sources,
    ILogger<WatchService> log)
{
    public async Task<LookupResult> LookupAsync(string query, CancellationToken ct)
    {
        query = query.Trim();
        if (query.Length == 0) return LookupResult.NotFound(query);

        var existing = watchlist.Current.Find(query);
        if (existing is not null) return LookupResult.AlreadyWatched(existing);

        var isUrl = query.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    || query.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

        var candidates = new List<BoardCandidate>();

        foreach (var sourceId in sources.SourceIds)
        {
            var resolver = sources.GetResolver(sourceId);
            if (resolver is null) continue;

            try
            {
                if (isUrl)
                {
                    var single = await resolver.ResolveByUrlAsync(query, ct);
                    if (single is not null) candidates.Add(single);
                }
                else
                {
                    candidates.AddRange(await resolver.ResolveByNameAsync(query, ct));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogWarning(ex, "Resolver {Source} has failed '{Query}'", sourceId, query);
            }
        }

        var known = watchlist.Current.Entries
            .Select(e => $"{e.VacancySourceId}/{e.BoardId}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var fresh = candidates
            .Where(c => !known.Contains($"{c.SourceId}/{c.BoardId}"))
            .OrderByDescending(c => c.Resolution == ResolutionKind.DirectSlug)
            .ThenByDescending(c => c.JobCount)
            .Take(5)
            .ToList();

        return fresh.Count == 0 ? LookupResult.NotFound(query) : LookupResult.Found(query, fresh);
    }

    public async Task<WatchEntry> AddAsync(BoardCandidate candidate, FilterSpec? filter, CancellationToken ct)
    {
        var entry = new WatchEntry
        {
            Id = $"{candidate.SourceId}:{candidate.BoardId}",
            VacancySourceId = candidate.SourceId,
            BoardId = candidate.BoardId,
            CompanyName = candidate.DisplayName,
            Enabled = true,
            CustomFilter = filter,
            
            // created unseeded - first cycle will be silent
            SeededAt = null
        };

        var added = await watchlist.AddAsync(entry, ct);
        log.LogInformation("Company added {Company} ({Source}/{Board})",
            added.CompanyName, added.VacancySourceId, added.BoardId);

        return added;
    }

    public Task<bool> RemoveAsync(string idOrName, CancellationToken ct)
    {
        var entry = watchlist.Current.Find(idOrName);
        return entry is null ? Task.FromResult(false) : watchlist.RemoveAsync(entry.Id, ct);
    }

    public IReadOnlyList<WatchEntry> List() => watchlist.Current.Entries;
}

public sealed record LookupResult
{
    public required LookupStatus Status { get; init; }
    public required string Query { get; init; }
    public IReadOnlyList<BoardCandidate> Candidates { get; init; } = [];
    public WatchEntry? Existing { get; init; }

    public static LookupResult Found(string query, IReadOnlyList<BoardCandidate> candidates) =>
        new() { Status = LookupStatus.Found, Query = query, Candidates = candidates };

    public static LookupResult NotFound(string query) =>
        new() { Status = LookupStatus.NotFound, Query = query };

    public static LookupResult AlreadyWatched(WatchEntry entry) =>
        new() { Status = LookupStatus.AlreadyWatched, Query = entry.CompanyName, Existing = entry };
}

public enum LookupStatus
{
    Found,
    NotFound,
    AlreadyWatched
}