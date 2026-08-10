using System.Net;
using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Infrastructure;
using JobsPulse.Sources.Ashby.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Sources.Ashby.Infrastructure;

public static class AshbyServiceCollectionExtensions
{
    public static IServiceCollection AddAshbySource(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<AshbyOptions>()
            .Bind(config.GetSection(AshbyOptions.SectionName));

        services.AddHttpClient(AshbyJobBoardClient.HttpClientName, (sp, http) =>
            {
                var opts = sp.GetRequiredService<IOptions<AshbyOptions>>().Value;
                http.BaseAddress = new Uri(opts.BaseUrl);
                // The whole board (descriptions included) arrives in one response and can be large.
                http.Timeout = TimeSpan.FromSeconds(60);
                http.DefaultRequestHeaders.UserAgent.ParseAdd("jobs-pulse-job-watcher/0.1");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddTransient(sp => new AshbyJobBoardClient(
            sp.GetRequiredService<IHttpClientFactory>()
                .CreateLoggingClient(AshbyJobBoardClient.HttpClientName, sp.GetRequiredService<ILog>()),
            sp.GetRequiredService<ILog>()));

        services.AddKeyedTransient<IVacancySource, AshbyBoardSource>(AshbyMapper.SourceId);
        services.AddKeyedTransient<IBoardResolver, AshbyBoardResolver>(AshbyMapper.SourceId);

        services.AddSingleton<AshbyMapper>();

        // Board discovery reads crawl indexes generically; this is the Ashby-specific part of it.
        services.AddSingleton<IBoardUrlParser, AshbyBoardUrlParser>();

        return services;
    }
}
