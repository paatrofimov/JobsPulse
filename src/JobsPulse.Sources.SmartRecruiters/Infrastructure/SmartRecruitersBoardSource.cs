using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Infrastructure;
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

    private async Task<IReadOnlyList<Vacancy>> MapAsync(
        SourceTarget target,
        IReadOnlyList<PostingDto> postings,
        SmartRecruitersOptions opts,
        CancellationToken ct)
    {
        var listed = postings.Select(p => mapper.ToVacancy(p, target.BoardId)).ToList();

        var decisions = DetailSelector.Select(
            listed, target, opts.IncludeContentOnPoll, opts.MaxDetailsPerPoll, SmartRecruitersMapper.ListUnchanged);

        var details = await DetailSelector.FetchAsync(decisions, opts.DetailConcurrency, async (i, token) =>
        {
            var response = await client.GetPostingAsync(target.BoardId, postings[i].Id, token);
            if (response.Success)
                return response.Value;

            ctxLog.Debug(
                "Posting {Posting} of {Board} has no readable detail ({Error})",
                postings[i].Id, target.BoardId, response.Error);

            return null;
        }, ct);

        var vacancies = new List<Vacancy>(postings.Count);

        for (var i = 0; i < postings.Count; i++)
        {
            vacancies.Add(decisions[i] switch
            {
                DetailDecision.Fetch when details[i] is { } detail => mapper.ToVacancy(postings[i], target.BoardId, detail),
                DetailDecision.Reuse => SmartRecruitersMapper.Reuse(listed[i], target.Known[listed[i].PostId]),
                _ => listed[i]
            });
        }

        ctxLog.Debug(
            "Board {Board}: {Fetched} details requested, {Reused} reused, {ListOnly} list-only",
            target.BoardId,
            decisions.Count(d => d == DetailDecision.Fetch),
            decisions.Count(d => d == DetailDecision.Reuse),
            decisions.Count(d => d == DetailDecision.ListOnly));

        return vacancies;
    }
}
