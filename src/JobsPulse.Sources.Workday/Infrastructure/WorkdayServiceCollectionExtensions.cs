using System.Net;
using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Infrastructure;
using JobsPulse.Sources.Workday.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Sources.Workday.Infrastructure;

public static class WorkdayServiceCollectionExtensions
{
    public static IServiceCollection AddWorkdaySource(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<WorkdayOptions>()
            .Bind(config.GetSection(WorkdayOptions.SectionName));

        services.AddHttpClient(WorkdayCareersSiteClient.HttpClientName, (sp, http) =>
            {
                var opts = sp.GetRequiredService<IOptions<WorkdayOptions>>().Value;

                // No base address: the host is part of the board address, so every url is absolute.
                http.Timeout = TimeSpan.FromSeconds(Math.Max(1, opts.RequestTimeoutSeconds));
                http.DefaultRequestHeaders.UserAgent.ParseAdd("jobs-pulse-job-watcher/0.1");
                http.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/html");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddTransient(sp => new WorkdayCxsClient(
            sp.GetRequiredService<IHttpClientFactory>()
                .CreateLoggingClient(WorkdayCareersSiteClient.HttpClientName, sp.GetRequiredService<ILog>()),
            sp.GetRequiredService<ILog>()));

        services.AddTransient(sp => new WorkdayCareersSiteClient(
            sp.GetRequiredService<IHttpClientFactory>()
                .CreateLoggingClient(WorkdayCareersSiteClient.HttpClientName, sp.GetRequiredService<ILog>()),
            sp.GetRequiredService<ILog>()));

        services.AddKeyedTransient<IVacancySource, WorkdayBoardSource>(WorkdayMapper.SourceId);
        services.AddKeyedTransient<IBoardResolver, WorkdayBoardResolver>(WorkdayMapper.SourceId);

        services.AddSingleton<WorkdayMapper>();

        // A crawled url carries a tenant hint rather than a confirmed tenant; the resolver adjudicates it during
        // validation, which is why Workday can take part in the crawl sweep at all.
        services.AddSingleton<IBoardUrlParser, WorkdayBoardUrlParser>();

        return services;
    }
}
