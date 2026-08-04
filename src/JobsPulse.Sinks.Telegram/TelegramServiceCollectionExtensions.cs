using JobsPulse.Core.Abstractions;
using JobsPulse.Sinks.Telegram.Bot;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace JobsPulse.Sinks.Telegram;

public static class TelegramServiceCollectionExtensions
{
    public static IServiceCollection AddTelegramSink(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<TelegramOptions>().Bind(config.GetSection(TelegramOptions.SectionName));

        services.AddHttpClient(TelegramClient.HttpClientName, (sp, http) =>
        {
            var opts = sp.GetRequiredService<IOptions<TelegramOptions>>().Value;
            http.BaseAddress = new Uri(opts.ApiBaseUrl);
            // Больше, чем long poll: иначе getUpdates будет рвать соединение раньше времени.
            http.Timeout = TimeSpan.FromSeconds(60);
        });

        services.AddSingleton(sp => new TelegramClient(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(TelegramClient.HttpClientName),
            sp.GetRequiredService<IOptions<TelegramOptions>>()));

        services.AddSingleton<IVacancySink, TelegramSink>();
        services.AddSingleton<PendingSelectionStore>();
        services.AddSingleton<CommandRouter>();
        services.AddHostedService<TelegramBotListener>();

        return services;
    }
}
