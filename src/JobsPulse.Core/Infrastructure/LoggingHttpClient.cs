using System.Diagnostics;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Core.Infrastructure;

/// <summary>
/// The single way out of the process. Wraps <see cref="HttpClient"/> so that every request is logged with its full
/// url, and every answer with its status code and how long it took - which is what makes «where did this request go
/// and how slow was it» readable in the log. Nothing else is added: no retries, no policies, no rate limiting.
/// </summary>
public sealed class LoggingHttpClient(HttpClient http, ILog log, string name)
{
    private readonly ILog ctxLog = log.ForContext($"http:{name}");

    /// <summary>Name of the underlying named client - the log context and the DI key.</summary>
    public string Name => name;

    public TimeSpan Timeout => http.Timeout;

    public Uri? BaseAddress => http.BaseAddress;

    public Task<HttpResponseMessage> GetAsync(string url, CancellationToken ct) =>
        GetAsync(url, HttpCompletionOption.ResponseContentRead, ct);

    public async Task<HttpResponseMessage> GetAsync(
        string url,
        HttpCompletionOption completionOption,
        CancellationToken ct)
    {
        // The relative url is resolved against the base address, so the log line is always the real target.
        var absolute = Absolute(url);

        ctxLog.Debug("GET {Url}", absolute);

        var watch = Stopwatch.StartNew();

        try
        {
            var response = await http.GetAsync(url, completionOption, ct);

            ctxLog.Debug(
                "GET {Url} answered HTTP {Status} in {Elapsed} ms",
                absolute, (int)response.StatusCode, watch.ElapsedMilliseconds);

            return response;
        }
        catch (Exception ex)
        {
            ctxLog.Debug(
                ex,
                "GET {Url} has failed after {Elapsed} ms: {Error}",
                absolute, watch.ElapsedMilliseconds, Describe(ex));

            throw;
        }
    }

    /// <summary>For the ATS whose list endpoint is a POST with a json body - Workday's careers backend.</summary>
    public async Task<HttpResponseMessage> PostAsync(string url, HttpContent content, CancellationToken ct)
    {
        var absolute = Absolute(url);

        ctxLog.Debug("POST {Url}", absolute);

        var watch = Stopwatch.StartNew();

        try
        {
            var response = await http.PostAsync(url, content, ct);

            ctxLog.Debug(
                "POST {Url} answered HTTP {Status} in {Elapsed} ms",
                absolute, (int)response.StatusCode, watch.ElapsedMilliseconds);

            return response;
        }
        catch (Exception ex)
        {
            ctxLog.Debug(
                ex,
                "POST {Url} has failed after {Elapsed} ms: {Error}",
                absolute, watch.ElapsedMilliseconds, Describe(ex));

            throw;
        }
    }

    private string Absolute(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute))
            return absolute.ToString();

        return http.BaseAddress is { } baseAddress && Uri.TryCreate(baseAddress, url, out var combined)
            ? combined.ToString()
            : url;
    }

    private static string Describe(Exception ex)
    {
        var innermost = ex;
        while (innermost.InnerException is not null)
            innermost = innermost.InnerException;

        return $"{ex.GetType().Name}: {innermost.Message}";
    }
}
