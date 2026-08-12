using JobsPulse.Core.Abstractions;
using JobsPulse.Sinks.Telegram.Pipeline;
using JobsPulse.Sinks.Telegram.Pipeline.Screens;
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
        services.AddSingleton<MessageFormatter>();

        // User interface: sessions, ownership and one screen per class.
        services.AddSingleton<UserSessionStore>();
        services.AddSingleton<WatchlistAccess>();
        services.AddSingleton<SystemWatchlistClaimer>();
        services.AddSingleton<VacancyPageBuilder>();
        services.AddSingleton<MainMenuScreen>();
        services.AddSingleton<WatchlistsScreen>();
        services.AddSingleton<WatchlistScreen>();
        services.AddSingleton<FilterScreen>();
        services.AddSingleton<CompaniesScreen>();
        services.AddSingleton<DisabledCompaniesScreen>();
        services.AddSingleton<AddCompanyScreen>();
        services.AddSingleton<VacanciesScreen>();
        services.AddSingleton<LanguageScreen>();
        services.AddSingleton<AdminScreen>();
        services.AddSingleton<ScreenRouter>();
        services.AddSingleton<BotUpdateHandler>();

        // Admin surface: raw ids and json, gated on Telegram:AdminUsernames.
        services.AddSingleton<PendingSelectionStore>();
        services.AddSingleton<CommandRouter>();

        services.AddHostedService<TelegramBotListener>();

        return services;
    }
}
