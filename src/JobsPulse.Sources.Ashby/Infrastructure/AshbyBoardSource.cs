using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Sources.Ashby.Options;
using Microsoft.Extensions.Options;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Sources.Ashby.Infrastructure;

public sealed class AshbyBoardSource(
    AshbyJobBoardClient client,
    AshbyMapper mapper,
    IOptionsMonitor<AshbyOptions> options,
    ILog log) : IVacancySource
{
    private readonly ILog ctxLog = log.ForContext<AshbyBoardSource>();

    public async Task<SourceTraverseResult> TraverseTargetAsync(SourceTarget target, CancellationToken ct)
    {
        var opts = options.CurrentValue;

        var response = await client.GetJobBoardAsync(target.BoardId, ct);

        if (response.NotFound)
            return SourceTraverseResult.Failed("board not found", boardMissing: true);

        if (!response.Success)
            return SourceTraverseResult.Failed(response.Error ?? "unknown error");

        var jobs = response.Value!.Jobs;

        var published = opts.IncludeUnlisted
            ? jobs
            : jobs.Where(j => j.IsListed).ToList();

        if (published.Count != jobs.Count)
        {
            ctxLog.Debug(
                "Board {Board}: {Skipped} of {Total} postings are unlisted and skipped",
                target.BoardId, jobs.Count - published.Count, jobs.Count);
        }

        // The whole board arrives in one response, so a successful answer is always a complete traversal.
        return SourceTraverseResult.Complete([.. published.Select(j => mapper.ToVacancy(j, target.BoardId))]);
    }
}
