using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model.Infrastructure;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Core.Pipeline;

/// <summary>
/// Keeps the stored vacancies in sync with the current filter. Every row carries the hash of the filter it passed;
/// when the filter changes, the rows are re-evaluated and the ones that no longer match are deleted.
/// Newly matching vacancies are not fetched here - the next polling cycle finds them by itself.
/// </summary>
public sealed class FilterMaintenanceService(
    IStateStore stateStore,
    IWatchlistProvider watchlistProvider,
    VacancyMatcher matcher,
    ILog log)
{
    private const int BatchLimit = 5000;

    private readonly ILog ctxLog = log.ForContext<FilterMaintenanceService>();

    public async Task<FilterMaintenanceReport> RunAsync(CancellationToken ct)
    {
        var watchlist = watchlistProvider.Current;

        var defaultFilter = watchlist.DefaultFilter;
        var defaultHash = VacancyHasher.ComputeFilterHash(defaultFilter);

        // Entries with their own filter store their own hash - those rows are not stale.
        var custom = watchlist.Entries
            .Where(e => e.CustomFilter is not null)
            .GroupBy(e => $"{e.VacancySourceId}/{e.BoardId}", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().CustomFilter!, StringComparer.OrdinalIgnoreCase);

        var hashes = custom.Values
            .Select(VacancyHasher.ComputeFilterHash)
            .Append(defaultHash)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var stale = await stateStore.LoadStaleFilterAsync(hashes, BatchLimit, ct);
        if (stale.Count == 0)
            return FilterMaintenanceReport.Empty;

        var obsolete = new List<VacancyKey>();
        var retained = new Dictionary<string, List<VacancyKey>>(StringComparer.Ordinal);

        foreach (var row in stale)
        {
            var vacancy = row.Vacancy;
            var board = $"{vacancy.SourceId}/{vacancy.BoardId}";
            var filter = custom.GetValueOrDefault(board, defaultFilter);

            if (!matcher.Matches(vacancy, Storable(filter)))
            {
                obsolete.Add(new VacancyKey(vacancy.SourceId, vacancy.BoardId, vacancy.PostId));
                continue;
            }

            var hash = VacancyHasher.ComputeFilterHash(filter);
            if (!retained.TryGetValue(hash, out var keys))
                retained[hash] = keys = [];

            keys.Add(new VacancyKey(vacancy.SourceId, vacancy.BoardId, vacancy.PostId));
        }

        var removed = await stateStore.DeleteAsync(obsolete, ct);

        var kept = 0;
        foreach (var (hash, keys) in retained)
            kept += await stateStore.SetFilterHashAsync(keys, hash, ct);

        ctxLog.Warn(
            "Filter change detected: {Checked} stored vacancies re-evaluated, {Removed} removed as not matching, {Kept} kept",
            stale.Count, removed, kept);

        return new FilterMaintenanceReport(stale.Count, removed, kept);
    }

    /// <summary>
    /// Descriptions are not persisted, so a description rule cannot be re-checked offline - it is dropped from the
    /// filter copy used here, otherwise every stored vacancy would look non-matching.
    /// </summary>
    private static FilterSpec Storable(FilterSpec filter) =>
        filter.DescriptionAnyOf.Count == 0 ? filter : filter with { DescriptionAnyOf = [] };
}
