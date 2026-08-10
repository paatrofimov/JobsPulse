using System.Text.RegularExpressions;
using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Helpers;
using JobsPulse.Core.Infrastructure;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Sources.Lever.Options;
using Microsoft.Extensions.Options;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Sources.Lever.Infrastructure;

public sealed partial class LeverBoardResolver(
    LeverPostingsClient client,
    IHttpClientFactory httpFactory,
    IOptionsMonitor<LeverOptions> options,
    ILog log) : IBoardResolver
{
    private readonly ILog ctxLog = log.ForContext<LeverBoardResolver>();

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
        // direct link to a Lever site
        var direct = LeverSiteSlug.ExtractFromUrl(url);
        if (direct is not null)
            return await ProbeAsync(direct, ct);

        // otherwise it is a career page
        try
        {
            var http = httpFactory.CreateLoggingClient(LeverPostingsClient.HttpClientName, log);
            using var response = await http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
                return null;

            var html = await response.Content.ReadAsStringAsync(ct);

            foreach (Match match in SiteUrlPattern().Matches(html))
            {
                var slug = match.Groups["slug"].Value;
                if (string.IsNullOrWhiteSpace(slug))
                    continue;

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
        var opts = options.CurrentValue;

        // Unfiltered on purpose: a probe answers «does this site exist», not «does it match the filter».
        var postings = await client.GetPostingsAsync(
            boardId,
            skip: 0,
            limit: Math.Clamp(opts.ProbePageSize, 1, 100),
            applyFilters: false,
            ct);

        // An unknown site answers with an empty array rather than 404, so emptiness is the only «does not exist»
        // signal there is - and a board without postings is worthless for discovery anyway.
        if (!postings.Success || postings.Value!.Count == 0)
            return null;

        return new BoardCandidate
        {
            SourceId = LeverMapper.SourceId,
            BoardId = boardId,
            DisplayName = boardId,
            JobCount = postings.Value!.Count,
            BoardUrl = $"https://jobs.eu.lever.co/{boardId}"
        };
    }

    [GeneratedRegex(@"jobs\.eu\.lever\.co/(?<slug>[a-z0-9_-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex SiteUrlPattern();
}