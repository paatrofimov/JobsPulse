using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model.Domain;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Sources.HeadHunter.Models;
using JobsPulse.Sources.HeadHunter.Options;
using Microsoft.Extensions.Options;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Sources.HeadHunter.Infrastructure;

/// <summary>
/// Reads one employer's vacancies through the common vacancy search filtered by `employer_id` - the platform publishes
/// no per-employer list endpoint. The board id is that employer id and nothing else: a traversal never searches for a
/// company, so a board discovered once is polled by id forever.
/// </summary>
public sealed class HeadHunterBoardSource(
    HeadHunterApiClient client,
    HeadHunterMapper mapper,
    IOptionsMonitor<HeadHunterOptions> options,
    ILog log) : IVacancySource
{
    private readonly ILog ctxLog = log.ForContext<HeadHunterBoardSource>();

    public async Task<SourceTraverseResult> TraverseTargetAsync(SourceTarget target, CancellationToken ct)
    {
        var opts = options.CurrentValue;
        var employerId = target.BoardId?.Trim();

        // Not a missing board: an unaddressable id is a configuration problem, and closing an employer's vacancies
        // over it would be wrong.
        if (string.IsNullOrWhiteSpace(employerId) || !employerId.All(char.IsAsciiDigit))
            return SourceTraverseResult.Failed($"'{target.BoardId}' is not a HeadHunter employer id");

        var page = await ReadPagesAsync(employerId, opts, ct);

        if (page.Error is not null)
            return SourceTraverseResult.Failed(page.Error, page.BoardMissing);

        var vacancies = await MapAsync(target, employerId, page.Items, opts, ct);

        if (page.IsComplete)
            return SourceTraverseResult.Complete(vacancies);

        ctxLog.Warn(
            "Employer {Employer}: traversal is incomplete after {Count} vacancies ({Reason})",
            employerId, vacancies.Count, page.Reason);

        return new SourceTraverseResult
        {
            IsComplete = false,
            Vacancies = vacancies,
            Error = $"{page.Reason}: {vacancies.Count} vacancies"
        };
    }

    /// <summary>
    /// Paging has two layers, because the search refuses to go arbitrarily deep: `page` * `per_page` past
    /// `MaxPagedItems` is an error, not an empty page. So pages are read until the last one of the *current* window,
    /// and an employer with more vacancies than the window can address is continued from the publication date of the
    /// oldest vacancy seen - which is why the order has to be publication time.
    ///
    /// Ids are deduplicated across windows: `date_to` is inclusive, so every window re-reads the boundary, and a
    /// window that brings nothing new is the end of the board however the counters read.
    /// </summary>
    private async Task<PagingOutcome> ReadPagesAsync(string employerId, HeadHunterOptions opts, CancellationToken ct)
    {
        var pageSize = Math.Clamp(opts.PageSize, 1, HeadHunterApiClient.MaxPageSize);
        var pagesPerWindow = Math.Max(1, Math.Max(pageSize, opts.MaxPagedItems) / pageSize);

        var items = new List<VacancyItemDto>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var skipped = 0;

        var pagesLeft = Math.Max(1, opts.MaxPages);
        var windows = Math.Max(1, opts.MaxDateWindows);

        DateTimeOffset? windowEnd = null;

        for (var window = 0; window < windows; window++)
        {
            DateTimeOffset? oldest = null;
            var fresh = 0;

            for (var page = 0; page < pagesPerWindow; page++)
            {
                ct.ThrowIfCancellationRequested();

                if (pagesLeft-- <= 0)
                    return Finish(false, $"page cap {opts.MaxPages} reached");

                var response = await client.SearchVacanciesAsync(
                    new HeadHunterVacancyQuery
                    {
                        EmployerId = employerId,
                        Page = page,
                        PerPage = pageSize,
                        DateTo = windowEnd,
                        OrderBy = opts.OrderBy
                    },
                    ct);

                if (response.NotFound)
                    return PagingOutcome.Missing();

                if (!response.Success)
                    return PagingOutcome.Failed(response.Error ?? "unknown error");

                var payload = response.Value!;
                var batch = payload.Items ?? [];

                foreach (var item in batch)
                {
                    var id = item.Id?.Trim();

                    if (string.IsNullOrWhiteSpace(id))
                    {
                        skipped++;
                        continue;
                    }

                    if (!seen.Add(id))
                        continue;

                    fresh++;
                    items.Add(item);

                    if (item.PublishedAt is { } published && (oldest is null || published < oldest))
                        oldest = published;
                }

                // The reported page count is what says the window is exhausted; a short page says the same thing
                // for a search that under-reports it.
                if (page + 1 >= (payload.Pages ?? 0) || batch.Count < pageSize)
                    return Finish(true, null);
            }

            if (oldest is null || fresh == 0)
            {
                // The window is full but brought nothing that could continue it - stopping here is the only honest
                // answer, and it is not a complete board.
                return Finish(false, "no publication date to continue paging from");
            }

            ctxLog.Debug(
                "Employer {Employer}: paging depth {Depth} reached after {Count} vacancies, continuing from {Date}",
                employerId, opts.MaxPagedItems, items.Count, oldest.Value);

            windowEnd = oldest;
        }

        return Finish(false, $"date window cap {opts.MaxDateWindows} reached");

        // The vacancies read so far are reported whatever the outcome - the orchestrator is what decides that an
        // incomplete traversal is not committed.
        PagingOutcome Finish(bool isComplete, string? reason)
        {
            if (skipped > 0)
                ctxLog.Debug("Employer {Employer}: {Skipped} vacancies carry no id and are dropped", employerId, skipped);

            return new PagingOutcome(items, isComplete, false, null, reason);
        }
    }

    /// <summary>
    /// The search carries a snippet rather than the ad, so a full description costs one request per vacancy. The budget
    /// bounds that: vacancies past it keep the snippet instead of turning a poll into a crawl.
    /// </summary>
    private async Task<IReadOnlyList<Vacancy>> MapAsync(
        SourceTarget target,
        string employerId,
        IReadOnlyList<VacancyItemDto> items,
        HeadHunterOptions opts,
        CancellationToken ct)
    {
        var withDetails = target.IncludeDescriptions || opts.IncludeContentOnPoll;
        var budget = withDetails ? Math.Max(0, opts.MaxDescriptionRequests) : 0;

        var vacancies = new List<Vacancy>(items.Count);
        var skipped = 0;

        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();

            VacancyDetailDto? detail = null;

            if (withDetails && budget > 0)
            {
                budget--;

                var response = await client.GetVacancyAsync(item.Id!, ct);

                if (response.Success)
                    detail = response.Value;
                else
                    ctxLog.Debug(
                        "Vacancy {Vacancy} of employer {Employer} has no readable detail ({Error})",
                        item.Id, employerId, response.Error ?? "vacancy is missing");
            }
            else if (withDetails)
            {
                skipped++;
            }

            if (mapper.ToVacancy(item, employerId, detail) is { } vacancy)
                vacancies.Add(vacancy);
        }

        if (skipped > 0)
        {
            ctxLog.Warn(
                "Employer {Employer}: {Skipped} vacancies keep the search snippet — request budget {Budget} is spent",
                employerId, skipped, opts.MaxDescriptionRequests);
        }

        return vacancies;
    }

    private readonly record struct PagingOutcome(
        List<VacancyItemDto> Items,
        bool IsComplete,
        bool BoardMissing,
        string? Error,
        string? Reason)
    {
        public static PagingOutcome Missing() => new([], false, true, "employer is missing", null);

        public static PagingOutcome Failed(string error) => new([], false, false, error, null);
    }
}
