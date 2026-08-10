using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JobsPulse.Core.Helpers;
using JobsPulse.Core.Infrastructure;
using JobsPulse.Sources.Ashby.Models;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Sources.Ashby.Infrastructure;

/// <summary>
/// Thin client over the public Ashby posting API: one request is the whole board, descriptions included.
/// No auth, no paging, no filtering - the endpoint takes the job board name and nothing else.
/// </summary>
public sealed class AshbyJobBoardClient(
    LoggingHttpClient http,
    ILog log)
{
    public const string HttpClientName = "ashby";

    private readonly ILog ctxLog = log.ForContext<AshbyJobBoardClient>();

    public async Task<AshbyFetch<JobBoardDto>> GetJobBoardAsync(string boardId, CancellationToken ct)
    {
        var url = Uri.EscapeDataString(boardId);

        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);

            // An unknown job board answers 404 - the only «does not exist» signal Ashby gives.
            if (response.StatusCode == HttpStatusCode.NotFound)
                return AshbyFetch<JobBoardDto>.Missing();

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(30);
                ctxLog.Warn("Ashby is throttling 429: {Url}, asked to wait for: {Delay}", url, retryAfter);
                return AshbyFetch<JobBoardDto>.Failure($"rate limited, retry after {retryAfter.TotalSeconds:F0}s");
            }

            if (!response.IsSuccessStatusCode)
                return AshbyFetch<JobBoardDto>.Failure($"HTTP {(int)response.StatusCode}");

            var payload = await response.Content.ReadFromJsonAsync<JobBoardDto>(
                JsonSerializerOptionsFactory.Instance, ct);

            return payload is null
                ? AshbyFetch<JobBoardDto>.Failure("empty response")
                : AshbyFetch<JobBoardDto>.Ok(payload);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            return AshbyFetch<JobBoardDto>.Failure(ex.Message);
        }
    }
}
