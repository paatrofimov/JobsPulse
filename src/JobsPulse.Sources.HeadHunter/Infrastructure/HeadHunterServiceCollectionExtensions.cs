using System.Net;
using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Infrastructure;
using JobsPulse.Sources.HeadHunter.Abstractions;
using JobsPulse.Sources.HeadHunter.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Sources.HeadHunter.Infrastructure;

public static class HeadHunterServiceCollectionExtensions
{
    public static IServiceCollection AddHeadHunterSource(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<HeadHunterOptions>()
            .Bind(config.GetSection(HeadHunterOptions.SectionName));

        services.AddHttpClient(HeadHunterApiClient.HttpClientName, (sp, http) =>
            {
                var opts = sp.GetRequiredService<IOptions<HeadHunterOptions>>().Value;

                http.BaseAddress = new Uri(opts.BaseUrl);
                http.Timeout = TimeSpan.FromSeconds(Math.Max(1, opts.RequestTimeoutSeconds));

                // The api refuses a user agent it does not like, so this header is part of the contract.
                var userAgent = HeadHunterUserAgent.Resolve(opts.UserAgent);

                if (!HeadHunterUserAgent.IsAcceptable(opts.UserAgent))
                {
                    sp.GetRequiredService<ILog>()
                        .ForContext(typeof(HeadHunterServiceCollectionExtensions))
                        .Warn(
                            "'Sources:HeadHunter:UserAgent' is empty or a placeholder ('{Configured}'), which HeadHunter "
                            + "blacklists - sending '{UserAgent}' instead. Name the installation and a real contact there.",
                            opts.UserAgent, userAgent);
                }

                http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
                http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton<IHeadHunterAuthorization, ConfiguredHeadHunterAuthorization>();

        // A singleton, unlike every other source client: the pacing state and the throttle penalty are the point of it,
        // and a per-request client would have neither.
        services.AddSingleton(sp => new HeadHunterApiClient(
            sp.GetRequiredService<IHttpClientFactory>()
                .CreateLoggingClient(HeadHunterApiClient.HttpClientName, sp.GetRequiredService<ILog>()),
            sp.GetRequiredService<IHeadHunterAuthorization>(),
            sp.GetRequiredService<IOptionsMonitor<HeadHunterOptions>>(),
            sp.GetRequiredService<ILog>()));

        services.AddSingleton<HeadHunterMapper>();

        services.AddKeyedTransient<IVacancySource, HeadHunterBoardSource>(HeadHunterMapper.SourceId);
        services.AddKeyedTransient<IBoardResolver, HeadHunterBoardResolver>(HeadHunterMapper.SourceId);

        // A crawled employer url carries the board id itself, so the crawl sweep needs nothing but the probe.
        services.AddSingleton<IBoardUrlParser, HeadHunterBoardUrlParser>();

        return services;
    }
}
