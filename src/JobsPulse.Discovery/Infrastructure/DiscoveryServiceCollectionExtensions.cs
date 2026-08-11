using System.Net;
using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Infrastructure;
using JobsPulse.Discovery.Abstractions;
using JobsPulse.Discovery.Options;
using JobsPulse.Discovery.Pipeline;
using JobsPulse.Discovery.Routines;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Discovery.Infrastructure;

public static class DiscoveryServiceCollectionExtensions
{
    public static IServiceCollection AddBoardDiscovery(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<DiscoveryOptions>()
            .Bind(config.GetSection(DiscoveryOptions.SectionName));

        services.AddHttpClient(CrawlIndexClient.HttpClientName, (sp, http) =>
            {
                var opts = sp.GetRequiredService<IOptions<DiscoveryOptions>>().Value;
                http.BaseAddress = new Uri(opts.IndexBaseUrl);
                // Index pages are streamed and can take minutes.
                http.Timeout = TimeSpan.FromMinutes(10);
                http.DefaultRequestHeaders.UserAgent.ParseAdd("jobs-pulse-job-watcher/0.1");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        // Only the path listings go through http - the parquet files themselves are read by DuckDB in place.
        services.AddHttpClient(CrawlIndexFileCatalog.HttpClientName, (sp, http) =>
            {
                var opts = sp.GetRequiredService<IOptions<DiscoveryOptions>>().Value;
                http.BaseAddress = new Uri(opts.Parquet.DataBaseUrl);
                http.Timeout = TimeSpan.FromMinutes(2);
                http.DefaultRequestHeaders.UserAgent.ParseAdd("jobs-pulse-job-watcher/0.1");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton<ICrawlIndexClient>(sp => new CrawlIndexClient(
            sp.GetRequiredService<IHttpClientFactory>()
                .CreateLoggingClient(CrawlIndexClient.HttpClientName, sp.GetRequiredService<ILog>()),
            sp.GetRequiredService<IOptionsMonitor<DiscoveryOptions>>(),
            sp.GetRequiredService<ILog>()));

        services.AddSingleton<ICrawlIndexFileCatalog>(sp => new CrawlIndexFileCatalog(
            sp.GetRequiredService<IHttpClientFactory>()
                .CreateLoggingClient(CrawlIndexFileCatalog.HttpClientName, sp.GetRequiredService<ILog>()),
            sp.GetRequiredService<IOptionsMonitor<DiscoveryOptions>>(),
            sp.GetRequiredService<ILog>()));

        services.AddSingleton<IParquetIndexClient, ParquetIndexClient>();

        services.AddSingleton<BoardTokenSink>();
        services.AddSingleton<ParquetIndexDiscoveryPass>();
        services.AddSingleton<HttpIndexDiscoveryPass>();

        services.AddSingleton<IBoardDiscoveryService, BoardDiscoveryService>();
        services.AddHostedService<BoardDiscoveryWorker>();

        return services;
    }
}
