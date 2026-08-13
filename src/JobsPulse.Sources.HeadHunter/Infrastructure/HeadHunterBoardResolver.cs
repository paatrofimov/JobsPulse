using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Sources.HeadHunter.Models;
using JobsPulse.Sources.HeadHunter.Options;
using Microsoft.Extensions.Options;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Sources.HeadHunter.Infrastructure;

/// <summary>
/// HeadHunter is a catalog rather than an ATS, and that changes what resolution is. There is no slug to guess and no
/// careers host to probe: the platform holds every employer under a numeric id, and finding a company means asking the
/// catalog for it. That id is the board id, so once discovery has answered, nothing ever searches for that company
/// again - `HeadHunterBoardSource` addresses the employer directly.
/// </summary>
public sealed class HeadHunterBoardResolver(
    HeadHunterApiClient client,
    IOptionsMonitor<HeadHunterOptions> options,
    ILog log) : IBoardResolver
{
    private readonly ILog ctxLog = log.ForContext<HeadHunterBoardResolver>();

    /// <summary>
    /// The employer search, ranked - see `HeadHunterEmployerMatcher` for why the top result cannot simply be taken and
    /// why an exact name cannot be required either.
    ///
    /// The verdict has three shapes:
    /// - an exact name, or a leader far enough ahead of the runner-up, is answered on its own;
    /// - a close field is answered whole, so the bot asks the user which company was meant instead of picking one;
    /// - a field where nothing is plausible is answered with nothing at all.
    /// </summary>
    public async Task<IReadOnlyList<BoardCandidate>> ResolveByNameAsync(string companyName, CancellationToken ct)
    {
        var opts = options.CurrentValue;

        if (string.IsNullOrWhiteSpace(companyName))
            return [];

        var response = await client.SearchEmployersAsync(
            companyName,
            page: 0,
            perPage: Math.Clamp(opts.EmployerSearchPageSize, 1, HeadHunterApiClient.MaxPageSize),
            onlyWithVacancies: opts.OnlyEmployersWithVacancies,
            ct);

        if (!response.Success)
        {
            ctxLog.Debug(
                "HeadHunter employer search for '{Company}' did not answer: {Error}",
                companyName, response.Error ?? "not found");

            return [];
        }

        var ranked = HeadHunterEmployerMatcher.Rank(companyName, response.Value!.Items ?? [])
            .Where(m => m.Score >= opts.MinMatchScore)
            .ToList();

        if (ranked.Count == 0)
        {
            ctxLog.Debug(
                "HeadHunter answered {Found} employers for '{Company}', none of them a plausible match",
                response.Value!.Found ?? 0, companyName);

            return [];
        }

        var best = ranked[0];
        var runnerUp = ranked.Count > 1 ? ranked[1].Score : 0;

        if (best.Score >= HeadHunterEmployerMatcher.ExactScore || best.Score - runnerUp >= opts.DecisiveScoreGap)
        {
            ctxLog.Debug(
                "HeadHunter employer {Employer} ({Score}) is the unambiguous answer for '{Company}', runner-up scored {RunnerUp}",
                best.Employer.Id, best.Score, companyName, runnerUp);

            return [ToCandidate(best, Resolution(best))];
        }

        ctxLog.Debug(
            "HeadHunter answered ambiguously for '{Company}': {Candidates} plausible employers, top scores {Best}/{RunnerUp}",
            companyName, ranked.Count, best.Score, runnerUp);

        return ranked
            .Take(Math.Max(1, opts.MaxEmployerCandidates))
            .Select(m => ToCandidate(m, Resolution(m)))
            .ToList();
    }

    /// <summary>
    /// An employer link is the id itself. A link to a single vacancy is the other half of «add a company by url» - it
    /// is how a company is shared - and it costs one request to learn which employer posted it.
    /// </summary>
    public async Task<BoardCandidate?> ResolveByUrlAsync(string url, CancellationToken ct)
    {
        var parts = HeadHunterUrl.Parse(url);
        if (parts is null)
            return null;

        if (parts.EmployerId is { } employerId)
            return await ProbeAsync(employerId, ct);

        if (parts.VacancyId is not { } vacancyId)
            return null;

        var response = await client.GetVacancyAsync(vacancyId, ct);

        if (!response.Success)
        {
            ctxLog.Debug(
                "HeadHunter vacancy {Vacancy} is not readable ({Error}) — the employer behind the link stays unknown",
                vacancyId, response.Error ?? "not found");

            return null;
        }

        var employer = response.Value!.Employer?.Id;
        if (string.IsNullOrWhiteSpace(employer))
            return null;

        var candidate = await ProbeAsync(employer, ct);

        return candidate is null
            ? null
            : candidate with { Resolution = ResolutionKind.CareersPage };
    }

    /// <summary>
    /// <paramref name="boardId"/> is an employer id. This is the validation step of both a manual `/board_add` and a
    /// token mined from a crawl index, and it is what a discovered id is re-read by - never a new search.
    /// </summary>
    public async Task<BoardCandidate?> ProbeAsync(string boardId, CancellationToken ct)
    {
        var employerId = boardId?.Trim();

        if (string.IsNullOrWhiteSpace(employerId) || !employerId.All(char.IsAsciiDigit))
        {
            ctxLog.Debug("'{Board}' is not a HeadHunter employer id — a numeric catalog id is expected", boardId);

            return null;
        }

        var response = await client.GetEmployerAsync(employerId, ct);

        if (!response.Success)
        {
            ctxLog.Debug(
                "HeadHunter employer {Employer} did not answer: {Error}",
                employerId, response.Error ?? "employer is missing");

            return null;
        }

        var employer = response.Value!;

        return new BoardCandidate
        {
            SourceId = HeadHunterMapper.SourceId,
            BoardId = employerId,
            DisplayName = string.IsNullOrWhiteSpace(employer.Name) ? employerId : employer.Name!.Trim(),
            // The employer id is the whole address, so there is nothing source-specific to carry along.
            Configuration = null,
            JobCount = employer.OpenVacancies ?? 0,
            BoardUrl = employer.AlternateUrl ?? $"https://hh.ru/employer/{employerId}",
            Resolution = ResolutionKind.Catalog
        };
    }

    /// <summary>
    /// A name found in a catalog is `Catalog`; only a name that matched exactly is reported as `DirectSlug`, which is
    /// what puts it first in the list the bot shows.
    /// </summary>
    private static ResolutionKind Resolution(HeadHunterEmployerMatch match) =>
        match.Score >= HeadHunterEmployerMatcher.ExactScore
            ? ResolutionKind.DirectSlug
            : ResolutionKind.Catalog;

    private static BoardCandidate ToCandidate(HeadHunterEmployerMatch match, ResolutionKind resolution) =>
        new()
        {
            SourceId = HeadHunterMapper.SourceId,
            BoardId = match.Employer.Id!,
            DisplayName = match.Employer.Name!.Trim(),
            Configuration = null,
            JobCount = match.OpenVacancies,
            BoardUrl = match.Employer.AlternateUrl ?? $"https://hh.ru/employer/{match.Employer.Id}",
            Resolution = resolution
        };
}
