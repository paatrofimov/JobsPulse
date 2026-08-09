using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Sources.Greenhouse.Options;
using Microsoft.Extensions.Options;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Sources.Greenhouse.Infrastructure;

public sealed class GreenhouseBoardSource(
    GreenhouseBoardClient client,
    GreenhouseMapper mapper,
    IOptions<GreenhouseOptions> options,
    ILog log) : IVacancySource
{
    private readonly ILog ctxLog = log.ForContext<GreenhouseBoardSource>();

    public async Task<SourceTraverseResult> TraverseTargetAsync(SourceTarget target, CancellationToken ct)
    {
        var includeContent = target.IncludeDescriptions || options.Value.IncludeContentOnPoll;

        var response = await client.GetJobsAsync(target.BoardId, includeContent, ct);

        if (response.NotFound)
            return SourceTraverseResult.Failed("board not found", boardMissing: true);

        if (!response.Success)
            return SourceTraverseResult.Failed(response.Error ?? "unknown error");

        var vacancies = response.Value!.Jobs
            .Select(jobDto => mapper.ToVacancy(jobDto, target.BoardId))
            .ToList();

        var expected = response.Value.Meta?.Total;
        if (expected is { } total && total != vacancies.Count)
        {
            ctxLog.Warn("Board {Board}: received {Actual} out of {Expected} — traversal is incomplete",
                target.BoardId, vacancies.Count, total);

            return new SourceTraverseResult
            {
                IsComplete = false,
                Vacancies = vacancies,
                Error = $"partial response: {vacancies.Count}/{total}"
            };
        }

        return SourceTraverseResult.Complete(vacancies);
    }
}