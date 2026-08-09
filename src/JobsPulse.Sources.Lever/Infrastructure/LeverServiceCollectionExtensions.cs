using System.Net;
using JobsPulse.Core.Abstractions;
using JobsPulse.Sources.Lever.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Sources.Lever.Infrastructure;

public static class LeverServiceCollectionExtensions
{
    public static IServiceCollection AddLeverSource(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<LeverOptions>()
            .Bind(config.GetSection(LeverOptions.SectionName));

        services.AddHttpClient(LeverPostingsClient.HttpClientName, (sp, http) =>
            {
                var opts = sp.GetRequiredService<IOptions<LeverOptions>>().Value;
                http.BaseAddress = new Uri(opts.BaseUrl);
                http.Timeout = TimeSpan.FromSeconds(30);
                http.DefaultRequestHeaders.UserAgent.ParseAdd("jobs-pulse-job-watcher/0.1");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddTransient(sp => new LeverPostingsClient(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(LeverPostingsClient.HttpClientName),
            sp.GetRequiredService<IOptionsMonitor<LeverOptions>>(),
            sp.GetRequiredService<ILog>()));

        services.AddKeyedTransient<IVacancySource, LeverBoardSource>(LeverMapper.SourceId);
        services.AddKeyedTransient<IBoardResolver, LeverBoardResolver>(LeverMapper.SourceId);

        services.AddSingleton<LeverMapper>();

        // Board discovery reads crawl indexes generically; this is the Lever-specific part of it.
        services.AddSingleton<IBoardUrlParser, LeverBoardUrlParser>();

        return services;
    }
}
