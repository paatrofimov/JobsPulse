using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model.Infrastructure;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Core.Pipeline;

/// <summary>
/// Everything the bot needs to manage watchlists: CRUD over <see cref="IWatchlistStorage"/> plus board resolution.
/// Resolution itself lives in the source projects (<see cref="IBoardResolver"/>); this service only orchestrates.
/// Every change is written to the database immediately - there is no in-memory copy of the configuration.
/// </summary>
public sealed class WatchService(
    IWatchlistStorage watchlists,
    ISourceCatalog sources,
    IPollingTrigger pollingTrigger,
    ILog log)
{
    private readonly ILog ctxLog = log.ForContext<WatchService>();

    public Task<IReadOnlyList<Watchlist>> ListAsync(CancellationToken ct) => watchlists.GetAllAsync(ct);

    /// <summary>Watchlists of one owner - «my watchlists» in the bot.</summary>
    public async Task<IReadOnlyList<Watchlist>> ListByOwnerAsync(long ownerUserId, CancellationToken ct) =>
        [.. (await watchlists.GetAllAsync(ct)).Where(w => w.OwnerUserId == ownerUserId)];

    /// <summary>A watchlist is addressed either by its numeric id or by its name.</summary>
    public async Task<Watchlist?> ResolveAsync(string reference, CancellationToken ct)
    {
        reference = reference.Trim();
        if (reference.Length == 0)
            return null;

        return long.TryParse(reference, out var id)
            ? await watchlists.GetAsync(id, ct)
            : await watchlists.FindByNameAsync(reference, ct);
    }

    /// <summary>A null owner creates a system watchlist - nobody can edit it from the bot except an admin.</summary>
    public async Task<Watchlist?> CreateAsync(string name, long? ownerUserId, CancellationToken ct)
    {
        var created = await watchlists.CreateAsync(name.Trim(), FilterSpec.MatchAll, ownerUserId, ct);
        if (created is not null)
            ctxLog.Info("Watchlist created: {Watchlist} (id {Id}, owner {Owner})", created.Name, created.Id, ownerUserId);

        return created;
    }

    public async Task<bool> RenameAsync(Watchlist watchlist, string name, CancellationToken ct)
    {
        var renamed = await watchlists.RenameAsync(watchlist.Id, name.Trim(), ct);
        if (renamed)
            ctxLog.Info("Watchlist {Id} renamed from {Old} to {New}", watchlist.Id, watchlist.Name, name.Trim());

        return renamed;
    }

    public async Task<bool> RemoveAsync(Watchlist watchlist, CancellationToken ct)
    {
        var removed = await watchlists.DeleteAsync(watchlist.Id, ct);
        if (removed)
            ctxLog.Info("Watchlist removed: {Watchlist} (id {Id})", watchlist.Name, watchlist.Id);

        return removed;
    }

    public Task<bool> SetEnabledAsync(Watchlist watchlist, bool enabled, CancellationToken ct) =>
        watchlists.SetEnabledAsync(watchlist.Id, enabled, ct);

    public async Task<bool> SetFilterAsync(Watchlist watchlist, FilterSpec filter, CancellationToken ct)
    {
        var updated = await watchlists.SetFilterAsync(watchlist.Id, filter, ct);
        if (!updated)
            return false;

        ctxLog.Info("Filter of watchlist {Watchlist} changed to [{Filter}]", watchlist.Name, filter);

        // The stored vacancies are re-evaluated at the start of the next cycle - start it now.
        pollingTrigger.RequestImmediateRun();

        return true;
    }

    public Task<bool> SetIntervalAsync(Watchlist watchlist, int? intervalMinutes, CancellationToken ct) =>
        watchlists.SetIntervalAsync(watchlist.Id, intervalMinutes, ct);

    /// <summary>Adds a board by explicit source and board id, probing the ATS for the display name when possible.</summary>
    public async Task<BoardAddResult> AddBoardAsync(
        Watchlist watchlist,
        string sourceId,
        string boardId,
        string? companyName,
        CancellationToken ct)
    {
        if (sources.GetSource(sourceId) is null)
            return BoardAddResult.UnknownSource(sourceId);

        var name = companyName?.Trim();

        // A probe is what fills in the source-specific configuration, so it also runs when the name is explicit.
        var candidate = await ProbeSafeAsync(sourceId, boardId, ct);

        if (string.IsNullOrWhiteSpace(name))
        {
            if (candidate is null)
                return BoardAddResult.BoardNotFound(boardId);

            name = candidate.DisplayName;
        }

        var entry = await watchlists.AddEntryAsync(
            watchlist.Id, sourceId, boardId, name!, candidate?.Configuration, ct);
        if (entry is null)
            return BoardAddResult.WatchlistNotFound();

        ctxLog.Info(
            "Board added to watchlist {Watchlist}: {Company} ({Source}/{Board})",
            watchlist.Name, entry.CompanyName, entry.VacancySourceId, entry.BoardId);

        // A brand new entry is due immediately -- do not wait for the next scheduled cycle.
        pollingTrigger.RequestImmediateRun();

        return BoardAddResult.Ok(entry);
    }

    public async Task<WatchlistEntry?> AddCandidateAsync(
        Watchlist watchlist,
        BoardCandidate candidate,
        CancellationToken ct)
    {
        var entry = await watchlists.AddEntryAsync(
            watchlist.Id, candidate.SourceId, candidate.BoardId, candidate.DisplayName, candidate.Configuration, ct);

        if (entry is null)
            return null;

        ctxLog.Info(
            "Board added to watchlist {Watchlist}: {Company} ({Source}/{Board})",
            watchlist.Name, entry.CompanyName, entry.VacancySourceId, entry.BoardId);

        pollingTrigger.RequestImmediateRun();

        return entry;
    }

    /// <summary>
    /// Drops a board from a watchlist. A discovered board is only disabled: the row is what tells the registry sweep
    /// the user does not want this board, so deleting it would let the next pass promote it right back.
    /// </summary>
    public async Task<EntryRemoveResult> RemoveEntryAsync(
        Watchlist watchlist,
        string entryReference,
        CancellationToken ct)
    {
        var entry = watchlist.FindEntry(entryReference);
        if (entry is null)
            return EntryRemoveResult.NotFound;

        if (entry.Origin == BoardOrigin.Discovery)
        {
            return await watchlists.SetEntryEnabledAsync(entry.Id, false, ct)
                ? EntryRemoveResult.Disabled
                : EntryRemoveResult.NotFound;
        }

        return await watchlists.RemoveEntryAsync(entry.Id, ct)
            ? EntryRemoveResult.Removed
            : EntryRemoveResult.NotFound;
    }

    public async Task<bool> SetEntryEnabledAsync(
        Watchlist watchlist,
        string entryReference,
        bool enabled,
        CancellationToken ct)
    {
        var entry = watchlist.FindEntry(entryReference);
        if (entry is null)
            return false;

        var updated = await watchlists.SetEntryEnabledAsync(entry.Id, enabled, ct);

        // A re-enabled board has no run stamp of its own, so it is due at once.
        if (updated && enabled)
            pollingTrigger.RequestImmediateRun();

        return updated;
    }

    /// <summary>Marks a company as worked through (a CV went out) or clears the mark.</summary>
    public Task<bool> SetEntryWorkedAsync(long entryId, bool worked, CancellationToken ct) =>
        watchlists.SetEntryWorkedAsync(entryId, worked, ct);

    /// <summary>Company name or career page url to board candidates, excluding what the watchlist already has.</summary>
    public async Task<LookupResult> LookupAsync(Watchlist watchlist, string query, CancellationToken ct)
    {
        query = query.Trim();
        if (query.Length == 0)
            return LookupResult.NotFound(query);

        ctxLog.Debug("Looking up boards for query '{Query}' (watchlist {Watchlist})", query, watchlist.Name);

        var existing = watchlist.FindEntry(query);
        if (existing is not null)
            return LookupResult.AlreadyWatched(existing);

        var isUrl = query.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    || query.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

        var candidates = new List<BoardCandidate>();

        foreach (var sourceId in sources.SourceIds)
        {
            var resolver = sources.GetResolver(sourceId);
            if (resolver is null)
                continue;

            try
            {
                if (isUrl)
                {
                    var single = await resolver.ResolveByUrlAsync(query, ct);
                    if (single is not null)
                        candidates.Add(single);
                }
                else
                {
                    candidates.AddRange(await resolver.ResolveByNameAsync(query, ct));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ctxLog.Warn(ex, "Resolver {Source} has failed '{Query}'", sourceId, query);
            }
        }

        // Only this watchlist filters out known boards - another watchlist may legitimately watch the same board.
        var known = watchlist.Entries
            .Select(e => e.BoardKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var fresh = candidates
            .Where(c => !known.Contains($"{c.SourceId}/{c.BoardId}"))
            .OrderByDescending(c => c.Resolution == ResolutionKind.DirectSlug)
            .ThenByDescending(c => c.JobCount)
            .Take(5)
            .ToList();

        return fresh.Count == 0 ? LookupResult.NotFound(query) : LookupResult.Found(query, fresh);
    }

    private async Task<BoardCandidate?> ProbeSafeAsync(string sourceId, string boardId, CancellationToken ct)
    {
        var resolver = sources.GetResolver(sourceId);
        if (resolver is null)
            return null;

        try
        {
            return await resolver.ProbeAsync(boardId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ctxLog.Warn(ex, "Probe of {Source}/{Board} has failed", sourceId, boardId);
            return null;
        }
    }
}
