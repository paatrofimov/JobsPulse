using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model.Infrastructure;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Core.Pipeline;

/// <summary>
/// Keeps the stored vacancies in sync with the current watchlist filters. Every row carries the hash of the filter
/// set it passed; when any watchlist filter changes, the rows are re-evaluated and the ones that no longer match any
/// watchlist are deleted. Every stale row is handled in one run, batch after batch. Newly matching vacancies are not
/// fetched here - the next polling cycle finds them.
///
/// The per-watchlist match layer is not touched: it is reconciled by the next poll of the board, which is also what
/// turns a narrowed filter into a Filtered notification for that watchlist.
/// </summary>
public sealed class FilterMaintenanceService(
    IStateStore stateStore,
    IWatchlistStorage watchlists,
    VacancyMatcher matcher,
    ILog log)
{
    private const int BatchLimit = 5000;

    private readonly ILog ctxLog = log.ForContext<FilterMaintenanceService>();

    public async Task<FilterMaintenanceReport> RunAsync(CancellationToken ct)
    {
        var plan = WatchlistPlan.Build(await watchlists.GetEnabledAsync(ct));

        // Everything would look stale with no filters at all - stored state is kept as is instead of being wiped.
        if (!plan.HasWatchlists)
        {
            ctxLog.Debug("No enabled watchlists — stored vacancies are left untouched");
            return FilterMaintenanceReport.Empty;
        }

        var storable = plan.StorageFilters.Select(Storable).ToList();
        var total = FilterMaintenanceReport.Empty;

        // Batch after batch until nothing stale is left: a row left for the next run would wait there for hours,
        // and the registry boards are not polled often enough to clean it up in the meantime.
        while (true)
        {
            var batch = await RunBatchAsync(plan, storable, ct);

            total = new FilterMaintenanceReport(
                total.Checked + batch.Checked,
                total.Removed + batch.Removed,
                total.Retained + batch.Retained);

            // A batch that changed nothing would be read again as it is - stop instead of spinning.
            if (batch.Checked < BatchLimit || batch.Removed + batch.Retained == 0)
                break;
        }

        if (total.Checked == 0)
            return total;

        ctxLog.Warn(
            "Filter change detected: {Checked} stored vacancies re-evaluated, {Removed} removed as not matching, {Kept} kept",
            total.Checked, total.Removed, total.Retained);

        return total;
    }

    private async Task<FilterMaintenanceReport> RunBatchAsync(
        WatchlistPlan plan,
        IReadOnlyList<FilterSpec> storable,
        CancellationToken ct)
    {
        string[] known = plan.MatchAllBoards.Count == 0
            ? [plan.StorageFilterHash]
            : [plan.StorageFilterHash, plan.MatchAllFilterHash];

        var stale = await stateStore.LoadStaleFilterAsync(known, BatchLimit, ct);
        if (stale.Count == 0)
            return FilterMaintenanceReport.Empty;

        var obsolete = new List<VacancyKey>();
        var retained = new List<VacancyKey>();
        var matchAll = new List<VacancyKey>();

        foreach (var row in stale)
        {
            var vacancy = row.Vacancy;
            var key = new VacancyKey(vacancy.SourceId, vacancy.BoardId, vacancy.PostId);

            if (plan.MatchAllBoards.Contains($"{vacancy.SourceId}/{vacancy.BoardId}"))
                matchAll.Add(key);
            else if (storable.Any(f => matcher.Matches(vacancy, f)))
                retained.Add(key);
            else
                obsolete.Add(key);
        }

        var removed = await stateStore.DeleteAsync(obsolete, ct);
        var kept = await stateStore.SetFilterHashAsync(retained, plan.StorageFilterHash, ct)
                   + await stateStore.SetFilterHashAsync(matchAll, plan.MatchAllFilterHash, ct);

        return new FilterMaintenanceReport(stale.Count, removed, kept);
    }

    /// <summary>
    /// Descriptions are not persisted, so a description rule cannot be re-checked offline - both of them are dropped
    /// from the filter copy used here, otherwise every stored vacancy would look non-matching (`AnyOf`) or every
    /// excluded one would look fine (`NoneOf`).
    /// </summary>
    private static FilterSpec Storable(FilterSpec filter) =>
        filter.DescriptionAnyOf.Count == 0 && filter.DescriptionNoneOf.Count == 0
            ? filter
            : filter with
            {
                DescriptionAnyOf = [],
                DescriptionNoneOf = []
            };
}
