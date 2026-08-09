using System.Text.RegularExpressions;
using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Sources.Greenhouse.Options;
using Microsoft.Extensions.Options;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Sources.Greenhouse.Infrastructure;

// todo (patrofimov) replace this with CommonCrawl json parsing for all available existing boards (background soft routine) 
public sealed partial class GreenhouseBoardResolver(
    GreenhouseBoardClient client,
    IHttpClientFactory httpFactory,
    IOptions<GreenhouseOptions> options,
    ILog log) : IBoardResolver
{
    private readonly ILog ctxLog = log.ForContext<GreenhouseBoardResolver>();

    public async Task<IReadOnlyList<BoardCandidate>> ResolveByNameAsync(string companyName, CancellationToken ct)
    {
        var guesses = SlugGuesser.Generate(companyName, options.Value.MaxSlugGuesses);
        var found = new List<BoardCandidate>();

        for (var i = 0; i < guesses.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var candidate = await ProbeAsync(guesses[i], ct);
            if (candidate is null) continue;

            found.Add(candidate with
            {
                Resolution = i == 0 ? ResolutionKind.DirectSlug : ResolutionKind.Guessed
            });

            if (i == 0) break;
        }

        return found;
    }

    public async Task<BoardCandidate?> ResolveByUrlAsync(string url, CancellationToken ct)
    {
        // direct link to Greenhouse board
        var direct = SlugGuesser.ExtractFromUrl(url);
        if (direct is not null) return await ProbeAsync(direct, ct);

        // otherwise it is career page
        try
        {
            using var http = httpFactory.CreateClient(GreenhouseBoardClient.HttpClientName);
            using var response = await http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
                return null;

            var html = await response.Content.ReadAsStringAsync(ct);

            foreach (Match match in BoardUrlPattern().Matches(html))
            {
                var slug = match.Groups["slug"].Value;
                if (string.IsNullOrWhiteSpace(slug)) continue;

                var candidate = await ProbeAsync(slug, ct);
                if (candidate is not null)
                    return candidate with { Resolution = ResolutionKind.CareersPage };
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ctxLog.Warn(ex, "Failed to resolve page by url {Url}", url);
        }

        return null;
    }

    public async Task<BoardCandidate?> ProbeAsync(string boardId, CancellationToken ct)
    {
        var board = await client.GetBoardAsync(boardId, ct);
        if (!board.Success) return null;

        var jobs = await client.GetJobsAsync(boardId, includeContent: false, ct);
        if (!jobs.Success) return null;

        return new BoardCandidate
        {
            SourceId = GreenhouseMapper.SourceId,
            BoardId = boardId,
            DisplayName = string.IsNullOrWhiteSpace(board.Value!.Name) ? boardId : board.Value.Name!.Trim(),
            JobCount = jobs.Value!.Meta?.Total ?? jobs.Value.Jobs.Count,
            BoardUrl = $"https://job-boards.greenhouse.io/{boardId}"
        };
    }

    [GeneratedRegex(
        @"(?:boards|job-boards)\.greenhouse\.io/(?:embed/job_board\?for=)?(?<slug>[a-z0-9_-]+)",
        RegexOptions.IgnoreCase)]
    private static partial Regex BoardUrlPattern();
}