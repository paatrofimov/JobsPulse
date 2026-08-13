using System.Net;
using System.Xml;
using JobsPulse.Core.Infrastructure;
using JobsPulse.Sources.SuccessFactors.Models;
using JobsPulse.Sources.SuccessFactors.Options;
using Microsoft.Extensions.Options;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Sources.SuccessFactors.Infrastructure;

/// <summary>
/// The seo sitemap of a career site, and the cheap way to ask a board how big it is.
///
/// '/sitemap.xml' is one of two documents depending on how the site is configured, so the root element is sniffed
/// rather than assumed: on most sites it is a '&lt;urlset&gt;' of exactly the job urls, on the rest it is the job feed
/// again. Both answer «does this domain publish a SuccessFactors board, and how many vacancies are on it» - which is
/// what a probe asks - and the url list does it without downloading a single description.
/// </summary>
public sealed class SuccessFactorsSitemapClient(
    LoggingHttpClient http,
    IOptionsMonitor<SuccessFactorsOptions> options,
    ILog log)
{
    private readonly ILog ctxLog = log.ForContext<SuccessFactorsSitemapClient>();

    public async Task<SuccessFactorsFetch<SuccessFactorsSiteSummary>> GetSummaryAsync(
        SuccessFactorsBoardConfig config,
        CancellationToken ct)
    {
        var opts = options.CurrentValue;
        var url = config.SitemapUrl;

        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return SuccessFactorsFetch<SuccessFactorsSiteSummary>.Missing();

            if (!response.IsSuccessStatusCode)
                return SuccessFactorsFetch<SuccessFactorsSiteSummary>.Failure($"HTTP {(int)response.StatusCode}");

            await using var body = await response.Content.ReadAsStreamAsync(ct);
            await using var budgeted = new ByteBudgetStream(body, Math.Max(1024, opts.MaxFeedBytes));

            try
            {
                return SuccessFactorsFetch<SuccessFactorsSiteSummary>.Ok(await ReadAsync(budgeted, ct));
            }
            catch (XmlException ex)
            {
                if (budgeted.BudgetExceeded)
                {
                    return SuccessFactorsFetch<SuccessFactorsSiteSummary>.TooLarge(
                        $"sitemap exceeds the {opts.MaxFeedBytes} byte budget");
                }

                return SuccessFactorsFetch<SuccessFactorsSiteSummary>.TooLarge($"sitemap is truncated: {ex.Message}");
            }
            catch (InvalidDataException ex)
            {
                ctxLog.Debug("Sitemap of {Url} is not a job list: {Error}", url, ex.Message);

                return SuccessFactorsFetch<SuccessFactorsSiteSummary>.Failure($"not a job sitemap: {ex.Message}");
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            return SuccessFactorsFetch<SuccessFactorsSiteSummary>.Failure(ex.Message);
        }
    }

    private static async Task<SuccessFactorsSiteSummary> ReadAsync(Stream stream, CancellationToken ct)
    {
        using var reader = XmlReader.Create(stream, SuccessFactorsFeedParser.ReaderSettings());

        var root = await SuccessFactorsFeedParser.ReadRootNameAsync(reader, ct)
                   ?? throw new InvalidDataException("the document is empty");

        // Some sites answer '/sitemap.xml' with the feed itself - then the cheap route is the expensive one, but the
        // answer is the same and there is nothing to gain by asking twice.
        if (root.Equals("rss", StringComparison.OrdinalIgnoreCase))
        {
            var feed = await SuccessFactorsFeedParser.ReadChannelAsync(reader, includeDescriptions: false, ct);

            return new SuccessFactorsSiteSummary
            {
                JobCount = feed.Items.Count,
                Title = feed.Title
            };
        }

        if (!root.Equals("urlset", StringComparison.OrdinalIgnoreCase) &&
            !root.Equals("sitemapindex", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"the document is <{root}>");
        }

        var urls = new List<string>();

        // Reading an element's content already advances the reader, so the walk must not advance again after one.
        while (!reader.EOF)
        {
            ct.ThrowIfCancellationRequested();

            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "loc")
            {
                if (!await reader.ReadAsync())
                    break;

                continue;
            }

            var loc = (await reader.ReadElementContentAsStringAsync()).Trim();

            // A sitemap holds the job pages and nothing else on these sites, but the check costs nothing and keeps a
            // content page from being counted as a vacancy.
            if (SuccessFactorsPostingIdentity.FromUrl(loc) is not null)
                urls.Add(loc);
        }

        return new SuccessFactorsSiteSummary
        {
            JobCount = urls.Count,
            JobUrls = urls
        };
    }
}
