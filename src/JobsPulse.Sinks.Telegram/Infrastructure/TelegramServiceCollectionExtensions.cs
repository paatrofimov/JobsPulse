using JobsPulse.Core.Abstractions;
using JobsPulse.Sinks.Telegram.Options;
using JobsPulse.Sinks.Telegram.Routines;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Telegram.Bot;

namespace JobsPulse.Sinks.Telegram.Infrastructure;

public static class TelegramServiceCollectionExtensions
{
    public static IServiceCollection AddTelegramSink(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<TelegramOptions>().Bind(config.GetSection(TelegramOptions.SectionName));

        services.AddSingleton<ITelegramBotClient>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<TelegramOptions>>().Value;
            return new TelegramBotClient(options.BotFatherToken);
        });
            
        services.AddSingleton<TelegramClientFacade>();

        services.AddSingleton<IVacancySink, TelegramSink>();
        services.AddSingleton<PendingSelectionStore>();
        services.AddSingleton<CommandRouter>();
        services.AddHostedService<TelegramBotListener>();

        return services;
    }
}