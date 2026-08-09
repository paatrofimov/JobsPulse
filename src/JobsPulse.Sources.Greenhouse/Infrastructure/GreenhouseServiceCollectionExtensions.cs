using System.Net;
using JobsPulse.Core.Abstractions;
using JobsPulse.Sources.Greenhouse.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Sources.Greenhouse.Infrastructure;

public static class GreenhouseServiceCollectionExtensions
{
    public static IServiceCollection AddGreenhouseSource(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<GreenhouseOptions>()
            .Bind(config.GetSection(GreenhouseOptions.SectionName));

        services.AddHttpClient(GreenhouseBoardClient.HttpClientName, (sp, http) =>
            {
                var opts = sp.GetRequiredService<IOptions<GreenhouseOptions>>().Value;
                http.BaseAddress = new Uri(opts.BaseUrl);
                http.Timeout = TimeSpan.FromSeconds(30);
                http.DefaultRequestHeaders.UserAgent.ParseAdd("jobs-pulse-job-watcher/0.1");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddTransient(sp => new GreenhouseBoardClient(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(GreenhouseBoardClient.HttpClientName),
            sp.GetRequiredService<ILog>()));

        services.AddKeyedTransient<IVacancySource, GreenhouseBoardSource>(GreenhouseMapper.SourceId);
        services.AddKeyedTransient<IBoardResolver, GreenhouseBoardResolver>(GreenhouseMapper.SourceId);

        services.AddSingleton<GreenhouseMapper>();

        // Board discovery reads crawl indexes generically; this is the Greenhouse-specific part of it.
        services.AddSingleton<IBoardUrlParser, GreenhouseBoardUrlParser>();

        return services;
    }
}