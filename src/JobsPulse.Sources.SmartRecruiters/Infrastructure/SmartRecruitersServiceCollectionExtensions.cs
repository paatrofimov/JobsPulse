using System.Net;
using JobsPulse.Core.Abstractions;
using JobsPulse.Sources.SmartRecruiters.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Sources.SmartRecruiters.Infrastructure;

public static class SmartRecruitersServiceCollectionExtensions
{
    public static IServiceCollection AddSmartRecruitersSource(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<SmartRecruitersOptions>()
            .Bind(config.GetSection(SmartRecruitersOptions.SectionName));

        services.AddHttpClient(SmartRecruitersPostingsClient.HttpClientName, (sp, http) =>
            {
                var opts = sp.GetRequiredService<IOptions<SmartRecruitersOptions>>().Value;
                http.BaseAddress = new Uri(opts.BaseUrl);
                http.Timeout = TimeSpan.FromSeconds(30);
                http.DefaultRequestHeaders.UserAgent.ParseAdd("jobs-pulse-job-watcher/0.1");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddTransient(sp => new SmartRecruitersPostingsClient(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(SmartRecruitersPostingsClient.HttpClientName),
            sp.GetRequiredService<IOptionsMonitor<SmartRecruitersOptions>>(),
            sp.GetRequiredService<ILog>()));

        services.AddKeyedTransient<IVacancySource, SmartRecruitersBoardSource>(SmartRecruitersMapper.SourceId);
        services.AddKeyedTransient<IBoardResolver, SmartRecruitersBoardResolver>(SmartRecruitersMapper.SourceId);

        services.AddSingleton<SmartRecruitersMapper>();

        // Board discovery reads crawl indexes generically; this is the SmartRecruiters-specific part of it.
        services.AddSingleton<IBoardUrlParser, SmartRecruitersBoardUrlParser>();

        return services;
    }
}
