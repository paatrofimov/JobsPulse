using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model.Infrastructure;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Core.Pipeline;

/// <summary>
/// Keeps the stored vacancies in sync with the current watchlist filters. Every row carries the hash of the filter
/// set it passed; when any watchlist filter changes, the rows are re-evaluated and the ones that no longer match any
/// watchlist are deleted. Newly matching vacancies are not fetched here - the next polling cycle finds them.
///
/// The per-watchlist match layer is not touched: it is reconciled by the next poll of the board, which is also what
/// turns a narrowed filter into a Closed notification for that watchlist.
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

        var stale = await stateStore.LoadStaleFilterAsync([plan.StorageFilterHash], BatchLimit, ct);
        if (stale.Count == 0)
            return FilterMaintenanceReport.Empty;

        var storable = plan.StorageFilters.Select(Storable).ToList();

        var obsolete = new List<VacancyKey>();
        var retained = new List<VacancyKey>();

        foreach (var row in stale)
        {
            var vacancy = row.Vacancy;
            var key = new VacancyKey(vacancy.SourceId, vacancy.BoardId, vacancy.PostId);

            if (storable.Any(f => matcher.Matches(vacancy, f)))
                retained.Add(key);
            else
                obsolete.Add(key);
        }

        var removed = await stateStore.DeleteAsync(obsolete, ct);
        var kept = await stateStore.SetFilterHashAsync(retained, plan.StorageFilterHash, ct);

        ctxLog.Warn(
            "Filter change detected: {Checked} stored vacancies re-evaluated, {Removed} removed as not matching, {Kept} kept",
            stale.Count, removed, kept);

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
