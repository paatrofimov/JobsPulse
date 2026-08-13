using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Infrastructure;
using JobsPulse.Sources.HeadHunter.Infrastructure;
using JobsPulse.Sources.HeadHunter.Options;
using Vostok.Logging.Abstractions;
using Vostok.Logging.Console;

namespace JobsPulse.Tests.Integration.HeadHunter;

/// <summary>
/// The source, the resolver and the api client over a stubbed transport. Nothing else - storage and the telegram sink
/// have nothing to do with reading a catalog.
/// </summary>
public sealed class HeadHunterTestHost : IDisposable
{
    private readonly HttpClient http;

    public HeadHunterTestHost(HeadHunterStubApi api, HeadHunterOptions? options = null)
    {
        var opts = options ?? Fast();
        var log = (ILog)new ConsoleLog();
        var monitor = new TestOptionsMonitor<HeadHunterOptions>(opts);

        http = new HttpClient(api) { BaseAddress = new Uri(opts.BaseUrl) };

        var client = new HeadHunterApiClient(
            new LoggingHttpClient(http, log, HeadHunterApiClient.HttpClientName),
            new ConfiguredHeadHunterAuthorization(monitor),
            monitor,
            log);

        Source = new HeadHunterBoardSource(client, new HeadHunterMapper(TimeProvider.System), monitor, log);
        Resolver = new HeadHunterBoardResolver(client, monitor, log);
    }

    public IVacancySource Source { get; }

    public IBoardResolver Resolver { get; }

    /// <summary>Pacing and retry delays are the client's job, not something every test should wait for.</summary>
    public static HeadHunterOptions Fast() => new()
    {
        PauseBetweenRequestsMsec = 0,
        Retries = 0,
        RetryDelaySeconds = 1,
        ThrottlePenaltyStepSeconds = 0
    };

    public void Dispose() => http.Dispose();
}
