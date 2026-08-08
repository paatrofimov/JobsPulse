using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Options;
using JobsPulse.Core.Pipeline;
using JobsPulse.Host.Infrastructure;
using JobsPulse.Host.Rouitines;
using JobsPulse.Sinks.Telegram.Infrastructure;
using JobsPulse.Sources.Greenhouse.Infrastructure;
using JobsPulse.Storage.Infrastructure;
using JobsPulse.Storage.Storages;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddJsonFile("watchlist.json", optional: true, reloadOnChange: false);

// Secrets: locally — user-secrets, prod — env variables (Telegram__BotToken).
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
    sp.GetRequiredService<ILogger<FileWatchlistProvider>>()));

builder.Services.AddSingleton<VacancyMatcher>();
builder.Services.AddSingleton<ChangeDetector>();
builder.Services.AddSingleton<PollingOrchestrator>();
builder.Services.AddSingleton<WatchService>();

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