using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JobsPulse.Core.Helpers;
using JobsPulse.Core.Infrastructure;
using JobsPulse.Sources.Greenhouse.Models;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Sources.Greenhouse.Infrastructure;

public sealed class GreenhouseBoardClient(
    LoggingHttpClient http,
    ILog log)
{
    public const string HttpClientName = "greenhouse";

    private readonly ILog ctxLog = log.ForContext<GreenhouseBoardClient>();

    /// <param name="includeContent">
    /// Asks for the description of every posting too. It makes the answer an order of magnitude heavier, so it is
    /// asked only when something reads the descriptions.
    /// </param>
    public async Task<BoardFetch<JobListResponse>> GetJobsAsync(
        string boardId, bool includeContent, CancellationToken ct)
    {
        var url = $"{Uri.EscapeDataString(boardId)}/jobs";
        if (includeContent)
            url += "?content=true";

        return await GetAsync<JobListResponse>(url, ct);
    }

    public async Task<BoardFetch<BoardDto>> GetBoardAsync(string boardId, CancellationToken ct) =>
        await GetAsync<BoardDto>(Uri.EscapeDataString(boardId), ct);

    private async Task<BoardFetch<T>> GetAsync<T>(string relativeUrl, CancellationToken ct) where T : class
    {
        try
        {
            using var response = await http.GetAsync(relativeUrl, HttpCompletionOption.ResponseHeadersRead, ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return BoardFetch<T>.Missing();

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(30);
                ctxLog.Warn("Greenhouse is throttling 429: {Url}, asked to wait for: {Delay}", relativeUrl, retryAfter);
                return BoardFetch<T>.Failure($"rate limited, retry after {retryAfter.TotalSeconds:F0}s");
            }

            if (!response.IsSuccessStatusCode)
                return BoardFetch<T>.Failure($"HTTP {(int)response.StatusCode}");

            var payload = await response.Content.ReadFromJsonAsync<T>(JsonSerializerOptionsFactory.Instance, ct);
            return payload is null
                ? BoardFetch<T>.Failure("empty response")
                : BoardFetch<T>.Ok(payload);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException or IOException)
        {
            return BoardFetch<T>.Failure(ex.Message);
        }
    }
}