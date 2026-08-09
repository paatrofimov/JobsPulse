using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Helpers;
using JobsPulse.Core.Infrastructure;
using JobsPulse.Core.Options;
using JobsPulse.Core.Pipeline;
using JobsPulse.Discovery.Infrastructure;
using JobsPulse.Host.Infrastructure;
using JobsPulse.Host.Rouitines;
using JobsPulse.Sinks.Telegram.Infrastructure;
using JobsPulse.Sources.Greenhouse.Infrastructure;
using JobsPulse.Storage.Infrastructure;
using JobsPulse.Storage.Storages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Console;
using Vostok.Logging.Abstractions;
using Vostok.Logging.Console;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

var builder = Host.CreateApplicationBuilder(args);

ConfigureLogging(builder);

builder.Configuration.AddJsonFile("watchlist.json", optional: true, reloadOnChange: false);

// Secrets: locally — user-secrets (Telegram:BotToken), prod — env variables (Telegram__BotToken).
builder.Configuration.AddEnvironmentVariables();

builder.Services.AddSingleton(TimeProvider.System);

builder.Services.Configure<WatchlistPollingOptions>(builder.Configuration.GetSection(WatchlistPollingOptions.SectionName));
builder.Services.Configure<DeliveryOptions>(builder.Configuration.GetSection(DeliveryOptions.SectionName));

// --- New ATS sources should be added below. ---
builder.Services.AddGreenhouseSource(builder.Configuration);

var registeredSources = new[] { GreenhouseMapper.SourceId };
builder.Services.AddSingleton<ISourceCatalog>(sp => new SourceCatalog(sp, registeredSources));

builder.Services.AddStorage(builder.Configuration, connectionStringName: "Postgres");

builder.Services.AddSingleton<IWatchlistProvider>(sp => new FileWatchlistProvider(
    Path.Combine(AppContext.BaseDirectory, "watchlist.json"),
    sp.GetRequiredService<TimeProvider>(),
    sp.GetRequiredService<ILog>()));

builder.Services.AddSingleton<IPollingTrigger, PollingTrigger>();
builder.Services.AddSingleton<VacancyMatcher>();
builder.Services.AddSingleton<ChangeDetector>();
builder.Services.AddSingleton<PollingOrchestrator>();
builder.Services.AddSingleton<WatchService>();

builder.Services.AddBoardDiscovery(builder.Configuration);

builder.Services.AddTelegramSink(builder.Configuration);

builder.Services.AddHostedService<PollingWorker>();
builder.Services.AddHostedService<OutboxDispatcher>();

var host = builder.Build();

await PrepareStorage(host);

host.Run();

async Task PrepareStorage(IHost h)
{
    await using var scope = h.Services.CreateAsyncScope();

    var db = scope.ServiceProvider
        .GetRequiredService<JobsPulseDbContext>();

    await db.Database.MigrateAsync();
}

void ConfigureLogging(HostApplicationBuilder hostApplicationBuilder)
{
    hostApplicationBuilder.Services.AddSingleton<ILog>(
        new CompositeLog(
            new ConsoleLog(),
            FileLogProvider.Create("main-log")
        )
    );
    hostApplicationBuilder.Logging.AddFilter<ConsoleLoggerProvider>(
        "Microsoft.Hosting",
        LogLevel.None);

    hostApplicationBuilder.Logging.AddFilter<ConsoleLoggerProvider>(
        "Microsoft.Extensions.Hosting",
        LogLevel.None);

    builder.Logging.AddFilter(
        "Microsoft.Extensions.Http",
        LogLevel.Warning);
}