using System.Net;
using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Infrastructure;
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
                // todo (patrofimov) global lever instance
                http.BaseAddress = new Uri("https://api.eu.lever.co/v0/postings/"); // eu Lever instance 
                http.Timeout = TimeSpan.FromSeconds(30);
                http.DefaultRequestHeaders.UserAgent.ParseAdd("jobs-pulse-job-watcher/0.1");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddTransient(sp => new LeverPostingsClient(
            sp.GetRequiredService<IHttpClientFactory>()
                .CreateLoggingClient(LeverPostingsClient.HttpClientName, sp.GetRequiredService<ILog>()),
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
