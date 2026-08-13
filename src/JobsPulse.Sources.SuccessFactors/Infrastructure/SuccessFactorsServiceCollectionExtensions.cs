using System.Net;
using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Infrastructure;
using JobsPulse.Sources.SuccessFactors.Abstractions;
using JobsPulse.Sources.SuccessFactors.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Sources.SuccessFactors.Infrastructure;

public static class SuccessFactorsServiceCollectionExtensions
{
    public static IServiceCollection AddSuccessFactorsSource(
        this IServiceCollection services,
        IConfiguration config)
    {
        services.AddOptions<SuccessFactorsOptions>()
            .Bind(config.GetSection(SuccessFactorsOptions.SectionName));

        services.AddHttpClient(SuccessFactorsFeedClient.HttpClientName, (sp, http) =>
            {
                var opts = sp.GetRequiredService<IOptions<SuccessFactorsOptions>>().Value;

                // No base address: every board is a host of its own, so every url is absolute.
                http.Timeout = TimeSpan.FromSeconds(Math.Max(1, opts.RequestTimeoutSeconds));
                http.DefaultRequestHeaders.UserAgent.ParseAdd("jobs-pulse-job-watcher/0.1");
                http.DefaultRequestHeaders.Accept.ParseAdd("application/xml, text/xml, text/html");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The feed is xml carrying html and compresses by an order of magnitude, so this is not a nicety.
                AutomaticDecompression = DecompressionMethods.All,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddTransient(sp => new SuccessFactorsFeedClient(
            Client(sp),
            sp.GetRequiredService<IOptionsMonitor<SuccessFactorsOptions>>(),
            sp.GetRequiredService<ILog>()));

        services.AddTransient(sp => new SuccessFactorsSitemapClient(
            Client(sp),
            sp.GetRequiredService<IOptionsMonitor<SuccessFactorsOptions>>(),
            sp.GetRequiredService<ILog>()));

        services.AddTransient(sp => new SuccessFactorsCareerPortalClient(
            Client(sp),
            sp.GetRequiredService<ILog>()));

        services.AddTransient(sp => new SuccessFactorsCareersPageClient(
            Client(sp),
            sp.GetRequiredService<ILog>()));

        services.AddSingleton<SuccessFactorsMapper>();

        services.AddTransient<ISuccessFactorsListStrategy, CsbFeedStrategy>();

        services.AddKeyedTransient<IVacancySource, SuccessFactorsBoardSource>(SuccessFactorsMapper.SourceId);
        services.AddKeyedTransient<IBoardResolver, SuccessFactorsBoardResolver>(SuccessFactorsMapper.SourceId);

        // A crawled url carries a tenant, and a tenant is not the site that publishes the jobs; the resolver does
        // that translation during validation, which is what lets this source take part in the crawl sweep at all.
        services.AddSingleton<IBoardUrlParser, SuccessFactorsBoardUrlParser>();

        return services;
    }

    private static LoggingHttpClient Client(IServiceProvider sp) =>
        sp.GetRequiredService<IHttpClientFactory>()
            .CreateLoggingClient(SuccessFactorsFeedClient.HttpClientName, sp.GetRequiredService<ILog>());
}
