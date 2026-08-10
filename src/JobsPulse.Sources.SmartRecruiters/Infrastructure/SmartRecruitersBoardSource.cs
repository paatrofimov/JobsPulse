using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model.Domain;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Sources.SmartRecruiters.Models;
using JobsPulse.Sources.SmartRecruiters.Options;
using Microsoft.Extensions.Options;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Sources.SmartRecruiters.Infrastructure;

public sealed class SmartRecruitersBoardSource(
    SmartRecruitersPostingsClient client,
    SmartRecruitersMapper mapper,
    IOptionsMonitor<SmartRecruitersOptions> options,
    ILog log) : IVacancySource
{
    private readonly ILog ctxLog = log.ForContext<SmartRecruitersBoardSource>();

    public async Task<SourceTraverseResult> TraverseTargetAsync(SourceTarget target, CancellationToken ct)
    {
        var opts = options.CurrentValue;

        var pageSize = Math.Clamp(opts.PageSize, 1, 100);
        var postings = new List<PostingDto>();
        var complete = false;

        for (var page = 0; page < Math.Max(1, opts.MaxPages); page++)
        {
            ct.ThrowIfCancellationRequested();

            var response = await client.GetPostingsAsync(
                target.BoardId,
                offset: page * pageSize,
                limit: pageSize,
                applyFilters: true,
                ct);

            if (response.NotFound)
                return SourceTraverseResult.Failed("board not found", boardMissing: true);

            if (!response.Success)
                return SourceTraverseResult.Failed(response.Error ?? "unknown error");

            var payload = response.Value!;
            postings.AddRange(payload.Content);

            // `totalFound` is authoritative; a short page is the last one either way.
            if (payload.Content.Count < pageSize || postings.Count >= payload.TotalFound)
            {
                complete = true;
                break;
            }
        }

        var vacancies = await MapAsync(target, postings, opts, ct);

        if (complete)
            return SourceTraverseResult.Complete(vacancies);

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

    /// <summary>
    /// Descriptions and the job id are not in the list, so they cost one request per posting. The budget bounds
    /// that: postings past it are mapped without a description instead of turning the traversal into a crawl.
    /// </summary>
    private async Task<IReadOnlyList<Vacancy>> MapAsync(
        SourceTarget target,
        IReadOnlyList<PostingDto> postings,
        SmartRecruitersOptions opts,
        CancellationToken ct)
    {
        var withDetails = target.IncludeDescriptions || opts.IncludeContentOnPoll;
        var budget = withDetails ? Math.Max(0, opts.MaxDescriptionRequests) : 0;

        var vacancies = new List<Vacancy>(postings.Count);
        var skipped = 0;

        foreach (var posting in postings)
        {
            ct.ThrowIfCancellationRequested();

            PostingDetailDto? detail = null;

            if (withDetails && budget > 0)
            {
                budget--;

                var response = await client.GetPostingAsync(target.BoardId, posting.Id, ct);
                if (response.Success)
                    detail = response.Value;
                else
                    ctxLog.Debug(
                        "Posting {Posting} of {Board} has no readable detail ({Error})",
                        posting.Id, target.BoardId, response.Error);
            }
            else if (withDetails)
            {
                skipped++;
            }

            vacancies.Add(mapper.ToVacancy(posting, target.BoardId, detail));
        }

        if (skipped > 0)
            ctxLog.Warn(
                "Board {Board}: {Skipped} postings are mapped without a description — request budget {Budget} is spent",
                target.BoardId, skipped, opts.MaxDescriptionRequests);

        return vacancies;
    }
}
