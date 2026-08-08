using JobsPulse.Core.Abstractions;
using JobsPulse.Sinks.Telegram.Routines;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot;

namespace JobsPulse.Sinks.Telegram.Infrastructure;

public static class TelegramServiceCollectionExtensions
{
    public static IServiceCollection AddTelegramSink(this IServiceCollection services, IConfiguration config)
    {
        var token = config["Telegram:BotToken"] ?? throw new ArgumentNullException("Missing 'Telegram:BotToken' secret");

        services.AddSingleton<ITelegramBotClient>(_ => new TelegramBotClient(token));

        services.AddSingleton<TelegramClientFacade>();

        services.AddSingleton<IVacancySink, TelegramSink>();
        services.AddSingleton<PendingSelectionStore>();
        services.AddSingleton<CommandRouter>();
        services.AddSingleton<MessageFormatter>();
        services.AddHostedService<TelegramBotListener>();

        return services;
    }
}