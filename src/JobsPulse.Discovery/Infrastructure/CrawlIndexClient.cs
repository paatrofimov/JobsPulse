using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using JobsPulse.Discovery.Abstractions;
using JobsPulse.Discovery.Models;
using JobsPulse.Discovery.Options;
using Microsoft.Extensions.Options;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Discovery.Infrastructure;

public sealed partial class CrawlIndexClient(
    HttpClient http,
    IOptionsMonitor<DiscoveryOptions> options,
    ILog log) : ICrawlIndexClient
{
    public const string HttpClientName = "common-crawl-index";
    private const string CollectionsPath = "collinfo.json";

    private readonly ILog ctxLog = log.ForContext<CrawlIndexClient>();

    public async Task<IReadOnlyList<CrawlCollection>> GetCollectionsAsync(CancellationToken ct)
    {
        using var response = await SendAsync(CollectionsPath, ct);
        if (response is null || !response.IsSuccessStatusCode)
        {
            ctxLog.Warn("Failed to read crawl collections: HTTP {Status}", (int?)response?.StatusCode ?? 0);
            return [];
        }

        var dtos = await response.Content.ReadFromJsonAsync<List<CrawlCollectionDto>>(ct)
                   ?? [];

        return dtos
            .Where(d => !string.IsNullOrWhiteSpace(d.Id) && !string.IsNullOrWhiteSpace(d.CdxApi))
            .Select(d => new CrawlCollection
            {
                Id = d.Id!,
                Name = d.Name,
                CdxApiUrl = d.CdxApi!,
                Year = ParseYear(d.Id!)
            })
            .OrderByDescending(c => c.Id, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<CrawlCollection?> GetLatestCollectionAsync(CancellationToken ct) =>
        (await GetCollectionsAsync(ct)).FirstOrDefault();

    public async Task<int> GetPageCountAsync(CrawlIndexQuery query, CancellationToken ct)
    {
        using var response = await SendAsync(BuildUrl(query, page: null, showNumPages: true), ct);
        if (response is null || !response.IsSuccessStatusCode)
            return 0;

        try
        {
            var pages = await response.Content.ReadFromJsonAsync<CrawlIndexPagesDto>(ct);
            return pages?.Pages ?? 0;
        }
        catch (JsonException)
        {
            // The index answers with plain text when the pattern has no captures at all.
            return 0;
        }
    }

    public async IAsyncEnumerable<CrawlIndexRecord> StreamPageAsync(
        CrawlIndexQuery query,
        int page,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var response = await SendAsync(BuildUrl(query, page, showNumPages: false), ct);

        if (response is null || !response.IsSuccessStatusCode)
        {
            ctxLog.Warn(
                "Index page {Page} of {Collection} is not readable: HTTP {Status}",
                page, query.Collection.Id, (int?)response?.StatusCode ?? 0);

            yield break;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (line.Length == 0)
                continue;

            var record = TryParseRecord(line);
            if (record is not null)
                yield return record;
        }
    }

    private CrawlIndexRecord? TryParseRecord(string line)
    {
        try
        {
            var dto = JsonSerializer.Deserialize<CrawlIndexRecordDto>(line);
            if (string.IsNullOrWhiteSpace(dto?.Url))
                return null;

            return new CrawlIndexRecord
            {
                Url = dto.Url,
                Timestamp = dto.Timestamp,
                Status = dto.Status
            };
        }
        catch (JsonException)
        {
            ctxLog.Debug("Skipped unparsable index line");
            return null;
        }
    }

    private static string BuildUrl(CrawlIndexQuery query, int? page, bool showNumPages)
    {
        var url = $"{query.Collection.CdxApiUrl}?url={Uri.EscapeDataString(query.UrlPattern)}"
                  + "&output=json&fl=url,timestamp,status";

        if (!string.IsNullOrWhiteSpace(query.StatusFilter))
            url += $"&filter={Uri.EscapeDataString($"=status:{query.StatusFilter}")}";

        url += $"&pageSize={query.PageSize}";

        if (showNumPages)
            return url + "&showNumPages=true";

        return page is null ? url : url + $"&page={page}";
    }

    /// <summary>The index front-end throttles hard and answers 503 under load - retrying is the normal path.</summary>
    private async Task<HttpResponseMessage?> SendAsync(string url, CancellationToken ct)
    {
        var opts = options.CurrentValue;

        for (var attempt = 0; attempt <= opts.IndexRetries; attempt++)
        {
            HttpResponseMessage? response = null;

            try
            {
                response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);

                if (response.IsSuccessStatusCode || !IsTransient(response.StatusCode))
                    return response;

                ctxLog.Debug("Crawl index answered {Status} for {Url}, retrying", (int)response.StatusCode, url);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                ctxLog.Debug(ex, "Crawl index request has failed: {Url}", url);
            }

            response?.Dispose();

            if (attempt == opts.IndexRetries)
                break;

            await Task.Delay(TimeSpan.FromSeconds(opts.IndexRetryDelaySeconds * (attempt + 1)), ct);
        }

        return null;
    }

    private static bool IsTransient(HttpStatusCode status) =>
        status is HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;

    private static int ParseYear(string collectionId)
    {
        var match = YearPattern().Match(collectionId);
        return match.Success
            ? int.Parse(match.Groups["year"].Value, CultureInfo.InvariantCulture)
            : 0;
    }

    [GeneratedRegex(@"(?<year>(19|20)\d{2})")]
    private static partial Regex YearPattern();
}
