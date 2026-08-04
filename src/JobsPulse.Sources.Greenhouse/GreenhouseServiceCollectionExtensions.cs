using System.Net;
using JobsPulse.Core.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobsPulse.Sources.Greenhouse;

public static class GreenhouseServiceCollectionExtensions
{
    /// <summary>
    /// Подключение Greenhouse как источника. Добавление следующего ATS выглядит ровно так же —
    /// свой AddXxx, свои keyed-регистрации, ядро не меняется.
    /// </summary>
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
            sp.GetRequiredService<IOptions<GreenhouseOptions>>(),
            sp.GetRequiredService<ILogger<GreenhouseBoardClient>>()));

        // Transient, а не Singleton: внутри живёт HttpClient из фабрики,
        // и его нельзя удерживать вечно — иначе теряется ротация соединений.
        services.AddKeyedTransient<IVacancySource, GreenhouseBoardSource>(GreenhouseMapper.SourceId);
        services.AddKeyedTransient<IBoardResolver, GreenhouseBoardResolver>(GreenhouseMapper.SourceId);

        return services;
    }
}
