using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Sources.SuccessFactors.Abstractions;
using JobsPulse.Sources.SuccessFactors.Models;
using JobsPulse.Sources.SuccessFactors.Options;
using Microsoft.Extensions.Options;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Sources.SuccessFactors.Infrastructure;

/// <summary>
/// One board traversal. Everything variant-specific is in the strategies; this class only decides which of them is
/// asked and how their answers add up.
///
/// The strategies are tried in <see cref="ISuccessFactorsListStrategy.Priority"/> order and the first whole board
/// wins. A strategy that answers <see cref="SuccessFactorsFetch{T}.Truncated"/> - a feed too big to read in one piece,
/// or one the site cut off - is the case the ordering exists for: the board is real and answering, the cheap way of
/// reading it just does not fit, so the next strategy gets its turn instead of the board being reported as broken.
///
/// A board is only <see cref="SourceTraverseResult.BoardMissing"/> when every strategy that could serve it agreed it
/// is not there. That is deliberately hard to reach: a missing board closes every vacancy of the board, and a branded
/// career domain belongs to the customer, so it can answer 403 or 503 for reasons that have nothing to do with the
/// board being gone.
/// </summary>
public sealed class SuccessFactorsBoardSource(
    IEnumerable<ISuccessFactorsListStrategy> strategies,
    IOptionsMonitor<SuccessFactorsOptions> options,
    ILog log) : IVacancySource
{
    private readonly ILog ctxLog = log.ForContext<SuccessFactorsBoardSource>();

    private readonly IReadOnlyList<ISuccessFactorsListStrategy> ordered =
        [.. strategies.OrderBy(s => s.Priority)];

    public async Task<SourceTraverseResult> TraverseTargetAsync(SourceTarget target, CancellationToken ct)
    {
        var opts = options.CurrentValue;

        // The configuration is the address; the board id is only its canonical rendering and the fallback for a row
        // written before configurations existed.
        var config = SuccessFactorsBoardConfig.FromJson(target.Configuration)
                     ?? SuccessFactorsBoardConfig.FromBoardId(target.BoardId);

        if (config is null)
        {
            // Not a missing board: the board may well exist, we just cannot address it.
            return SourceTraverseResult.Failed($"board configuration is unreadable for '{target.BoardId}'");
        }

        var applicable = ordered.Where(s => s.CanServe(config)).ToList();

        if (applicable.Count == 0)
        {
            return SourceTraverseResult.Failed(
                $"no strategy can serve a {config.Variant} board '{config.BoardId}'");
        }

        var includeDescriptions = target.IncludeDescriptions || opts.IncludeContentOnPoll;

        var missing = true;
        string? lastError = null;

        foreach (var strategy in applicable)
        {
            ct.ThrowIfCancellationRequested();

            var response = await strategy.FetchAsync(config, includeDescriptions, ct);

            if (response.Success)
            {
                var listing = response.Value!;

                ctxLog.Debug(
                    "Board {Board}: {Count} vacancies read by the {Strategy} strategy",
                    config.BoardId, listing.Vacancies.Count, listing.Strategy);

                return SourceTraverseResult.Complete(listing.Vacancies);
            }

            lastError = response.Error ?? "unknown error";

            // One strategy calling the site missing says nothing about the others - the feed and the html listing are
            // different routes of the same site, and only the html one exists on every generation of it.
            if (!response.NotFound)
                missing = false;

            if (applicable.Count > 1)
            {
                ctxLog.Debug(
                    "Board {Board}: the {Strategy} strategy did not serve it ({Error})",
                    config.BoardId, strategy.Name, lastError);
            }
        }

        return SourceTraverseResult.Failed(lastError ?? "unknown error", missing);
    }
}
