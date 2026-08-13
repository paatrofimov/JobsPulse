using System.Net;
using System.Text.RegularExpressions;
using JobsPulse.Core.Infrastructure;
using JobsPulse.Sources.SuccessFactors.Models;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Sources.SuccessFactors.Infrastructure;

/// <summary>
/// Reads a company's own careers page and answers with the SuccessFactors boards it points at.
///
/// This is what makes 'www.kmd.net/career' - the url a person actually has - resolvable at all. Such a page is not a
/// board and carries no tenant of its own; what it carries is the link the visitor is meant to click, and that link is
/// either the branded career site ('jobs.kmd.net/search/') or the legacy portal of the tenant
/// ('career5.successfactors.eu/career?company=kmd'). Both are addresses this source already knows how to probe, so the
/// page only has to be mined for candidates - it never decides anything.
///
/// Links are read out of the raw html rather than out of a parsed document on purpose: the interesting one is as often
/// in a script payload or a data attribute as in an anchor, and a candidate that is not a board simply fails its probe.
/// A page that cannot be read is not a failure either - many corporate sites answer a bot with a challenge page - so
/// the answer is an empty list and the caller falls back to guessing.
/// </summary>
public sealed partial class SuccessFactorsCareersPageClient(
    LoggingHttpClient http,
    ILog log)
{
    /// <summary>Any absolute http url in the markup, however it is quoted.</summary>
    [GeneratedRegex(
        @"https?://[a-z0-9][a-z0-9.\-]*\.[a-z]{2,}(?::\d+)?(?:/[^\s""'<>)\\]*)?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AbsoluteUrl();

    /// <summary>A careers page is markup and scripts; the links to the board are not at the end of it.</summary>
    private const int MaxBytes = 1024 * 1024;

    private readonly ILog ctxLog = log.ForContext<SuccessFactorsCareersPageClient>();

    /// <summary>
    /// The boards the page points at, branded domains first - a domain is the address itself, while a tenant costs the
    /// portal roundtrip that translates it. Empty when the page could not be read or names nothing.
    /// </summary>
    public async Task<IReadOnlyList<SuccessFactorsBoardConfig>> FindBoardsAsync(
        string pageUrl,
        int maxHints,
        CancellationToken ct)
    {
        var html = await ReadAsync(pageUrl, ct);

        if (html is null)
            return [];

        var domains = new List<SuccessFactorsBoardConfig>();
        var tenants = new List<SuccessFactorsBoardConfig>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in AbsoluteUrl().Matches(html))
        {
            var parts = SuccessFactorsBoardUrl.Parse(match.Value.TrimEnd('.', ',', ';'));

            if (parts is null)
                continue;

            var config = parts.ToConfig();

            // A bare domain matches every link on the page, so only a url whose path the platform owns counts as a
            // site here - the page's own navigation must not become a list of boards to probe.
            if (config.HasDomain && !HasPath(match.Value))
                continue;

            if (!seen.Add(config.BoardId))
                continue;

            (config.HasDomain ? domains : tenants).Add(config);
        }

        var hints = domains.Concat(tenants).Take(Math.Max(1, maxHints)).ToList();

        if (hints.Count > 0)
        {
            ctxLog.Debug(
                "Careers page {Url} points at {Count} possible board(s): {Boards}",
                pageUrl, hints.Count, string.Join(", ", hints.Select(h => h.BoardId)));
        }

        return hints;
    }

    private static bool HasPath(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        uri.AbsolutePath.Trim('/').Length > 0;

    /// <summary>Null for every answer that is not a readable page - the caller has a cheaper fallback than retrying.</summary>
    private async Task<string?> ReadAsync(string url, CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);

            if (!response.IsSuccessStatusCode)
            {
                ctxLog.Debug(
                    "Careers page {Url} answered HTTP {Status}", url, (int)response.StatusCode);

                return null;
            }

            await using var body = await response.Content.ReadAsStreamAsync(ct);
            await using var budgeted = new ByteBudgetStream(body, MaxBytes);
            using var reader = new StreamReader(budgeted);

            var html = await reader.ReadToEndAsync(ct);

            // Urls inside script payloads are escaped as '\/' - unescaping is what makes them matchable.
            return html.Replace("\\/", "/", StringComparison.Ordinal);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            ctxLog.Debug("Careers page {Url} could not be read: {Error}", url, ex.Message);

            return null;
        }
    }
}
