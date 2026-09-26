using System.Net.Http.Headers;
using System.Net.Http.Json;
using JobsPulse.Core.Abstractions;
using JobsPulse.Host.Options;
using Microsoft.Extensions.Options;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Host.Infrastructure;

public sealed class GitHubWorkflowTrigger(
    IHttpClientFactory httpFactory,
    IOptions<GitHubDispatchOptions> options,
    TimeProvider clock,
    ILog log) : IPollingTrigger
{
    public const string HttpClientName = "github-dispatch";

    private readonly ILog ctxLog = log.ForContext<GitHubWorkflowTrigger>();
    private readonly Lock sync = new();

    private DateTimeOffset lastDispatch = DateTimeOffset.MinValue;

    public void RequestImmediateRun()
    {
        var now = clock.GetUtcNow();

        lock (sync)
        {
            // A burst of bot edits must not queue a burst of workflow runs.
            if (now - lastDispatch < TimeSpan.FromSeconds(options.Value.CooldownSeconds))
                return;

            lastDispatch = now;
        }

        _ = DispatchAsync();
    }

    // The polling cycle lives in GitHub Actions in this role - nothing in-process waits on the trigger.
    public Task WaitAsync(TimeSpan period, CancellationToken ct)
    {
        return Task.Delay(period, ct);
    }

    private async Task DispatchAsync()
    {
        var opts = options.Value;

        try
        {
            var http = httpFactory.CreateClient(HttpClientName);

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"https://api.github.com/repos/{opts.Repository}/actions/workflows/{opts.Workflow}/dispatches");

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", opts.Token);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.UserAgent.ParseAdd("JobsPulse");
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            request.Content = JsonContent.Create(new { @ref = opts.Ref });

            using var response = await http.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                ctxLog.Info("Workflow {Workflow} is dispatched", opts.Workflow);
                return;
            }

            var body = await response.Content.ReadAsStringAsync();
            ctxLog.Warn("Workflow {Workflow} dispatch has failed: {Status} {Body}", opts.Workflow, (int)response.StatusCode, body);
        }
        catch (Exception ex)
        {
            ctxLog.Error(ex, "Workflow {Workflow} dispatch has failed", opts.Workflow);
        }
    }
}
