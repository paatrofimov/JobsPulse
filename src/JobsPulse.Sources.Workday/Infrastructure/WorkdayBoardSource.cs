using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model.Domain;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Sources.Workday.Models;
using JobsPulse.Sources.Workday.Options;
using Microsoft.Extensions.Options;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Sources.Workday.Infrastructure;

public sealed class WorkdayBoardSource(
    WorkdayCxsClient client,
    WorkdayMapper mapper,
    IOptionsMonitor<WorkdayOptions> options,
    ILog log) : IVacancySource
{
    private readonly ILog ctxLog = log.ForContext<WorkdayBoardSource>();

    public async Task<SourceTraverseResult> TraverseTargetAsync(SourceTarget target, CancellationToken ct)
    {
        var opts = options.CurrentValue;

        // The configuration is the address; the board id is only its canonical rendering and the fallback for a row
        // written before configurations existed.
        var config = WorkdayBoardConfig.FromJson(target.Configuration)
                     ?? WorkdayBoardConfig.FromBoardId(target.BoardId);

        if (config is null)
        {
            // Not a missing board: the board may well exist, we just cannot address it.
            return SourceTraverseResult.Failed($"board configuration is unreadable for '{target.BoardId}'");
        }

        var page = await ReadPagesAsync(config, opts, ct);

        if (page.Error is not null)
            return SourceTraverseResult.Failed(page.Error, page.BoardMissing);

        var vacancies = await MapAsync(target, config, page.Postings, opts, ct);

        if (page.IsComplete)
            return SourceTraverseResult.Complete(vacancies);

        ctxLog.Warn(
            "Board {Board}: page cap {Cap} reached after {Count} postings — traversal is incomplete",
            config.BoardId, opts.MaxPages, page.Postings.Count);

        return new SourceTraverseResult
        {
            IsComplete = false,
            Vacancies = vacancies,
            Error = $"page cap reached: {page.Postings.Count} postings"
        };
    }

    private async Task<PagingOutcome> ReadPagesAsync(
        WorkdayBoardConfig config,
        WorkdayOptions opts,
        CancellationToken ct)
    {
        var pageSize = Math.Clamp(opts.PageSize, 1, WorkdayCxsClient.MaxPageSize);

        var postings = new List<(JobPostingDto Dto, string ExternalPath)>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Only the first page reports a trustworthy total - later pages have been observed answering zero.
        var total = int.MaxValue;
        var skipped = 0;

        for (var page = 0; page < Math.Max(1, opts.MaxPages); page++)
        {
            ct.ThrowIfCancellationRequested();

            var response = await client.GetJobsAsync(config, offset: page * pageSize, limit: pageSize, ct);

            if (response.NotFound)
                return PagingOutcome.Missing();

            if (!response.Success)
                return PagingOutcome.Failed(response.Error ?? "unknown error");

            var batch = response.Value!.JobPostings ?? [];
            if (batch.Count == 0)
                return Complete(config, postings, skipped);

            // A total smaller than the page it arrived with contradicts itself and would truncate the board, so it
            // is ignored and paging falls back to the short-page and no-new-postings rules.
            if (page == 0 && response.Value!.Total is { } reported && reported >= batch.Count)
                total = reported;

            var fresh = 0;

            foreach (var dto in batch)
            {
                var externalPath = dto.ExternalPath?.Trim();

                if (string.IsNullOrWhiteSpace(externalPath))
                {
                    skipped++;
                    continue;
                }

                if (!seenPaths.Add(externalPath))
                    continue;

                fresh++;
                postings.Add((dto, externalPath));
            }

            // Some tenants cap their result set and wrap back to the first page instead of answering an empty one,
            // so a page that brings nothing new is the end of the board however the total reads.
            if (fresh == 0)
                return Complete(config, postings, skipped);

            // Reaching the reported total is a complete traversal even when the total is the tenant's own cap:
            // treating it as incomplete would mean never committing state for a large board.
            if (batch.Count < pageSize || (page + 1) * pageSize >= total)
                return Complete(config, postings, skipped);
        }

        return Report(config, postings, skipped, isComplete: false);
    }

    private PagingOutcome Complete(
        WorkdayBoardConfig config,
        IReadOnlyList<(JobPostingDto Dto, string ExternalPath)> postings,
        int skipped) => Report(config, postings, skipped, isComplete: true);

    private PagingOutcome Report(
        WorkdayBoardConfig config,
        IReadOnlyList<(JobPostingDto Dto, string ExternalPath)> postings,
        int skipped,
        bool isComplete)
    {
        if (skipped > 0)
        {
            ctxLog.Debug(
                "Board {Board}: {Skipped} postings carry no external path and are dropped",
                config.BoardId, skipped);
        }

        return new PagingOutcome(postings, isComplete, false, null);
    }

    /// <summary>
    /// Descriptions and the only real posting date live on the per-vacancy endpoint, so they cost one request each.
    /// The budget bounds that: postings past it are mapped from the list alone instead of turning a poll into a crawl.
    /// </summary>
    private async Task<IReadOnlyList<Vacancy>> MapAsync(
        SourceTarget target,
        WorkdayBoardConfig config,
        IReadOnlyList<(JobPostingDto Dto, string ExternalPath)> postings,
        WorkdayOptions opts,
        CancellationToken ct)
    {
        var withDetails = target.IncludeDescriptions || opts.IncludeContentOnPoll;
        var budget = withDetails ? Math.Max(0, opts.MaxDescriptionRequests) : 0;

        var vacancies = new List<Vacancy>(postings.Count);
        var skipped = 0;

        foreach (var (dto, externalPath) in postings)
        {
            ct.ThrowIfCancellationRequested();

            JobPostingInfoDto? detail = null;

            if (withDetails && budget > 0)
            {
                budget--;

                var response = await client.GetJobAsync(config, externalPath, ct);

                if (response.Success)
                    detail = response.Value!.JobPostingInfo;
                else
                    ctxLog.Debug(
                        "Posting {Path} of {Board} has no readable detail ({Error})",
                        externalPath, config.BoardId, response.Error ?? "board is missing");
            }
            else if (withDetails)
            {
                skipped++;
            }

            vacancies.Add(mapper.ToVacancy(dto, config, externalPath, detail));
        }

        if (skipped > 0)
        {
            ctxLog.Warn(
                "Board {Board}: {Skipped} postings are mapped without a description — request budget {Budget} is spent",
                config.BoardId, skipped, opts.MaxDescriptionRequests);
        }

        return vacancies;
    }

    private readonly record struct PagingOutcome(
        IReadOnlyList<(JobPostingDto Dto, string ExternalPath)> Postings,
        bool IsComplete,
        bool BoardMissing,
        string? Error)
    {
        public static PagingOutcome Missing() => new([], false, true, "board not found");

        public static PagingOutcome Failed(string error) => new([], false, false, error);
    }
}
