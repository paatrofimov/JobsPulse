using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model.Domain;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Sources.Lever.Models;
using JobsPulse.Sources.Lever.Options;
using Microsoft.Extensions.Options;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Sources.Lever.Infrastructure;

public sealed class LeverBoardSource(
    LeverPostingsClient client,
    LeverMapper mapper,
    IOptionsMonitor<LeverOptions> options,
    ILog log) : IVacancySource
{
    private readonly ILog ctxLog = log.ForContext<LeverBoardSource>();

    public async Task<SourceTraverseResult> TraverseTargetAsync(SourceTarget target, CancellationToken ct)
    {
        var opts = options.CurrentValue;

        var pageSize = Math.Clamp(opts.PageSize, 1, 100);
        var vacancies = new List<Vacancy>();

        // Resolved by the first page and cached, so neither paging nor this lookup re-probes the instances.
        LeverRegion? region = null;

        for (var page = 0; page < Math.Max(1, opts.MaxPages); page++)
        {
            ct.ThrowIfCancellationRequested();

            var response = await client.GetPostingsAsync(
                target.BoardId,
                skip: page * pageSize,
                limit: pageSize,
                applyFilters: true,
                ct);

            if (response.NotFound)
                return SourceTraverseResult.Failed("board not found", boardMissing: true);

            if (!response.Success)
                return SourceTraverseResult.Failed(response.Error ?? "unknown error");

            var postings = response.Value!;
            if (postings.Count == 0)
                return SourceTraverseResult.Complete(vacancies);

            var home = region ??= await client.GetRegionAsync(target.BoardId, ct) ?? client.DefaultRegion;
            vacancies.AddRange(postings.Select(p => mapper.ToVacancy(p, target.BoardId, home)));

            // A short page is the last one - the API has no total count.
            if (postings.Count < pageSize)
                return SourceTraverseResult.Complete(vacancies);
        }

        ctxLog.Warn(
            "Board {Board}: page cap {Cap} reached after {Count} postings — traversal is incomplete",
            target.BoardId, opts.MaxPages, vacancies.Count);

        return new SourceTraverseResult
        {
            IsComplete = false,
            Vacancies = vacancies,
            Error = $"page cap reached: {vacancies.Count} postings"
        };
    }
}
