using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using JobsPulse.Core.Helpers;
using JobsPulse.Core.Infrastructure;
using JobsPulse.Sources.Lever.Models;
using JobsPulse.Sources.Lever.Options;
using Microsoft.Extensions.Options;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Sources.Lever.Infrastructure;

/// <summary>
/// Thin client over the Lever postings API. Unlike Greenhouse it pages (`skip`/`limit`) and accepts server-side
/// filters, so a narrow watchlist does not have to download the whole board.
/// </summary>
public sealed class LeverPostingsClient(
    LoggingHttpClient http,
    IOptionsMonitor<LeverOptions> options,
    ILog log)
{
    public const string HttpClientName = "lever";

    private readonly ILog ctxLog = log.ForContext<LeverPostingsClient>();

    public async Task<LeverFetch<List<PostingDto>>> GetPostingsAsync(
        string site,
        int skip,
        int limit,
        bool applyFilters,
        CancellationToken ct)
    {
        var opts = options.CurrentValue;

        var url = new StringBuilder($"{Uri.EscapeDataString(site)}?mode=json&skip={skip}&limit={limit}");

        if (applyFilters)
        {
            AppendFilter(url, "location", opts.Location);
            AppendFilter(url, "team", opts.Team);
            AppendFilter(url, "department", opts.Department);
            AppendFilter(url, "commitment", opts.Commitment);
            AppendFilter(url, "level", opts.Level);
        }

        return await GetAsync(url.ToString(), ct);
    }

    private async Task<LeverFetch<List<PostingDto>>> GetAsync(string relativeUrl, CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync(relativeUrl, HttpCompletionOption.ResponseHeadersRead, ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return LeverFetch<List<PostingDto>>.Missing();

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(30);
                ctxLog.Warn("Lever is throttling 429: {Url}, asked to wait for: {Delay}", relativeUrl, retryAfter);
                return LeverFetch<List<PostingDto>>.Failure($"rate limited, retry after {retryAfter.TotalSeconds:F0}s");
            }

            if (!response.IsSuccessStatusCode)
                return LeverFetch<List<PostingDto>>.Failure($"HTTP {(int)response.StatusCode}");

            var payload = await response.Content.ReadFromJsonAsync<List<PostingDto>>(
                JsonSerializerOptionsFactory.Instance, ct);

            return payload is null
                ? LeverFetch<List<PostingDto>>.Failure("empty response")
                : LeverFetch<List<PostingDto>>.Ok(payload);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            return LeverFetch<List<PostingDto>>.Failure(ex.Message);
        }
    }

    private static void AppendFilter(StringBuilder url, string name, IReadOnlyList<string> values)
    {
        // Repeated keys are OR-ed by the API.
        foreach (var value in values.Where(v => !string.IsNullOrWhiteSpace(v)))
            url.Append($"&{name}={Uri.EscapeDataString(value)}");
    }
}
