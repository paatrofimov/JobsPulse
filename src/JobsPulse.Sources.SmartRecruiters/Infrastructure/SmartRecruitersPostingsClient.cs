using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using JobsPulse.Core.Helpers;
using JobsPulse.Sources.SmartRecruiters.Models;
using JobsPulse.Sources.SmartRecruiters.Options;
using Microsoft.Extensions.Options;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Sources.SmartRecruiters.Infrastructure;

/// <summary>
/// Thin client over the public SmartRecruiters posting API: one request is one page, or one posting detail.
/// The list endpoint carries no description, so text costs an extra request per posting.
/// </summary>
public sealed class SmartRecruitersPostingsClient(
    HttpClient http,
    IOptionsMonitor<SmartRecruitersOptions> options,
    ILog log)
{
    public const string HttpClientName = "smartrecruiters";

    private readonly ILog ctxLog = log.ForContext<SmartRecruitersPostingsClient>();

    public async Task<SmartRecruitersFetch<PostingListDto>> GetPostingsAsync(
        string company,
        int offset,
        int limit,
        bool applyFilters,
        CancellationToken ct)
    {
        var opts = options.CurrentValue;

        var url = new StringBuilder($"{Uri.EscapeDataString(company)}/postings?offset={offset}&limit={limit}");

        if (applyFilters)
        {
            AppendFilter(url, "q", opts.Query);
            AppendFilter(url, "country", opts.Country);
            AppendFilter(url, "region", opts.Region);
            AppendFilter(url, "city", opts.City);
            AppendFilter(url, "department", opts.Department);
            AppendFilter(url, "language", opts.Language);
        }

        return await GetAsync<PostingListDto>(url.ToString(), ct);
    }

    public async Task<SmartRecruitersFetch<PostingDetailDto>> GetPostingAsync(
        string company,
        string postingId,
        CancellationToken ct) =>
        await GetAsync<PostingDetailDto>(
            $"{Uri.EscapeDataString(company)}/postings/{Uri.EscapeDataString(postingId)}", ct);

    private async Task<SmartRecruitersFetch<T>> GetAsync<T>(string relativeUrl, CancellationToken ct) where T : class
    {
        try
        {
            using var response = await http.GetAsync(relativeUrl, HttpCompletionOption.ResponseHeadersRead, ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return SmartRecruitersFetch<T>.Missing();

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(30);
                ctxLog.Warn("SmartRecruiters is throttling 429: {Url}, asked to wait for: {Delay}", relativeUrl, retryAfter);
                return SmartRecruitersFetch<T>.Failure($"rate limited, retry after {retryAfter.TotalSeconds:F0}s");
            }

            if (!response.IsSuccessStatusCode)
                return SmartRecruitersFetch<T>.Failure($"HTTP {(int)response.StatusCode}");

            var payload = await response.Content.ReadFromJsonAsync<T>(JsonSerializerOptionsFactory.Instance, ct);

            return payload is null
                ? SmartRecruitersFetch<T>.Failure("empty response")
                : SmartRecruitersFetch<T>.Ok(payload);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            return SmartRecruitersFetch<T>.Failure(ex.Message);
        }
    }

    private static void AppendFilter(StringBuilder url, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            url.Append($"&{name}={Uri.EscapeDataString(value)}");
    }
}
