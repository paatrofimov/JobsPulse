using System.Text.RegularExpressions;
using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Helpers;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Sources.SmartRecruiters.Options;
using Microsoft.Extensions.Options;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Sources.SmartRecruiters.Infrastructure;

public sealed partial class SmartRecruitersBoardResolver(
    SmartRecruitersPostingsClient client,
    IHttpClientFactory httpFactory,
    IOptionsMonitor<SmartRecruitersOptions> options,
    ILog log) : IBoardResolver
{
    private readonly ILog ctxLog = log.ForContext<SmartRecruitersBoardResolver>();

    public async Task<IReadOnlyList<BoardCandidate>> ResolveByNameAsync(string companyName, CancellationToken ct)
    {
        var guesses = CompanySlugGuesser.Generate(companyName, options.CurrentValue.MaxSlugGuesses);
        var found = new List<BoardCandidate>();

        for (var i = 0; i < guesses.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var candidate = await ProbeAsync(guesses[i], ct);
            if (candidate is null)
                continue;

            found.Add(candidate with
            {
                Resolution = i == 0 ? ResolutionKind.DirectSlug : ResolutionKind.Guessed
            });

            if (i == 0)
                break;
        }

        return found;
    }

    public async Task<BoardCandidate?> ResolveByUrlAsync(string url, CancellationToken ct)
    {
        // direct link to a SmartRecruiters career site
        var direct = SmartRecruitersCompanySlug.ExtractFromUrl(url);
        if (direct is not null)
            return await ProbeAsync(direct, ct);

        // otherwise it is a career page
        try
        {
            using var http = httpFactory.CreateClient(SmartRecruitersPostingsClient.HttpClientName);
            using var response = await http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
                return null;

            var html = await response.Content.ReadAsStringAsync(ct);

            foreach (Match match in CompanyUrlPattern().Matches(html))
            {
                var slug = match.Groups["slug"].Value;
                if (string.IsNullOrWhiteSpace(slug))
                    continue;

                var candidate = await ProbeAsync(slug.ToLowerInvariant(), ct);
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
        var opts = options.CurrentValue;

        // Unfiltered on purpose: a probe answers «does this company exist», not «does it match the filter».
        var postings = await client.GetPostingsAsync(
            boardId,
            offset: 0,
            limit: Math.Clamp(opts.ProbePageSize, 1, 100),
            applyFilters: false,
            ct);

        // An unknown company answers `totalFound: 0` rather than 404, so emptiness is the only «does not exist»
        // signal there is - and a company without postings is worthless for discovery anyway.
        if (!postings.Success || postings.Value!.TotalFound == 0)
            return null;

        var name = postings.Value!.Content.FirstOrDefault()?.Company?.Name;

        return new BoardCandidate
        {
            SourceId = SmartRecruitersMapper.SourceId,
            BoardId = boardId,
            DisplayName = string.IsNullOrWhiteSpace(name) ? boardId : name.Trim(),
            JobCount = postings.Value!.TotalFound,
            BoardUrl = $"https://jobs.smartrecruiters.com/{boardId}"
        };
    }

    [GeneratedRegex(
        @"(?:jobs|careers)\.smartrecruiters\.com/(?<slug>[a-z0-9_-]+)",
        RegexOptions.IgnoreCase)]
    private static partial Regex CompanyUrlPattern();
}
