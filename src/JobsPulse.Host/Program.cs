using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Helpers;
using JobsPulse.Core.Infrastructure;
using JobsPulse.Core.Options;
using JobsPulse.Core.Pipeline;
using JobsPulse.Discovery.Infrastructure;
using JobsPulse.Discovery.Routines;
using JobsPulse.Host.Infrastructure;
using JobsPulse.Host.Models;
using JobsPulse.Host.Options;
using JobsPulse.Host.Pipeline;
using JobsPulse.Host.Routines;
using JobsPulse.Sinks.Telegram.Infrastructure;
using JobsPulse.Sinks.Telegram.Routines;
using JobsPulse.Sources.Ashby.Infrastructure;
using JobsPulse.Sources.Greenhouse.Infrastructure;
using JobsPulse.Sources.HeadHunter.Infrastructure;
using JobsPulse.Sources.Lever.Infrastructure;
using JobsPulse.Sources.SmartRecruiters.Infrastructure;
using JobsPulse.Sources.SuccessFactors.Infrastructure;
using JobsPulse.Sources.Workday.Infrastructure;
using JobsPulse.Storage.Infrastructure;
using JobsPulse.Storage.Storages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Console;
using Vostok.Logging.Abstractions;
using Vostok.Logging.Console;
using Vostok.Logging.File;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

var builder = Host.CreateApplicationBuilder(args);

// `--role <name>`: `all` (default) runs every routine in one long-living process, `bot` only the Telegram listener,
// the rest are one-shot jobs for a scheduler such as GitHub Actions.
var role = ParseRole(builder.Configuration["role"]);

ConfigureLogging(builder);

// The watchlist configuration lives in PostgreSQL - the config file only carries infrastructure settings.
// Secrets: locally — user-secrets (Telegram:BotToken), prod — env variables (Telegram__BotToken).
builder.Configuration.AddEnvironmentVariables();

builder.Services.AddSingleton(TimeProvider.System);

builder.Services.Configure<WatchlistPollingOptions>(builder.Configuration.GetSection(WatchlistPollingOptions.SectionName));
builder.Services.Configure<DeliveryOptions>(builder.Configuration.GetSection(DeliveryOptions.SectionName));

// --- New ATS sources should be added below. ---
builder.Services.AddLeverSource(builder.Configuration);
builder.Services.AddGreenhouseSource(builder.Configuration);
builder.Services.AddSmartRecruitersSource(builder.Configuration);
builder.Services.AddAshbySource(builder.Configuration);
builder.Services.AddWorkdaySource(builder.Configuration);
builder.Services.AddSuccessFactorsSource(builder.Configuration);
builder.Services.AddHeadHunterSource(builder.Configuration);

var registeredSources = new[]
{
    LeverMapper.SourceId,
    GreenhouseMapper.SourceId,
    SmartRecruitersMapper.SourceId,
    AshbyMapper.SourceId,
    WorkdayMapper.SourceId,
    SuccessFactorsMapper.SourceId,
    HeadHunterMapper.SourceId
};
builder.Services.AddSingleton<ISourceCatalog>(sp => new SourceCatalog(sp, registeredSources));

builder.Services.AddStorage(builder.Configuration, connectionStringName: "Postgres");

builder.Services.Configure<JobOptions>(builder.Configuration.GetSection(JobOptions.SectionName));
builder.Services.Configure<GitHubDispatchOptions>(builder.Configuration.GetSection(GitHubDispatchOptions.SectionName));

// Without an in-process polling loop the bot asks GitHub Actions for an immediate run instead.
if (role == HostRole.Bot && !string.IsNullOrWhiteSpace(builder.Configuration["GitHubDispatch:Token"]))
{
    builder.Services.AddHttpClient(GitHubWorkflowTrigger.HttpClientName);
    builder.Services.AddSingleton<IPollingTrigger, GitHubWorkflowTrigger>();
}
else
{
    builder.Services.AddSingleton<IPollingTrigger, PollingTrigger>();
}
builder.Services.Configure<RegistryPollingOptions>(builder.Configuration.GetSection(RegistryPollingOptions.SectionName));

// Progress of both cycles, read by the admin screen. In-memory, so it is measured from the start of the process.
builder.Services.AddSingleton<ITraversalProgressTracker, TraversalProgressTracker>();

builder.Services.AddSingleton<VacancyMatcher>();
builder.Services.AddSingleton<ChangeDetector>();
builder.Services.AddSingleton<BoardProcessor>();
builder.Services.AddSingleton<FilterMaintenanceService>();
builder.Services.AddSingleton<PollingOrchestrator>();
builder.Services.AddSingleton<DiscoveredBoardPromoter>();
builder.Services.AddSingleton<RegistryPollingService>();
builder.Services.AddSingleton<WatchService>();
builder.Services.AddSingleton<OutboxDelivery>();
builder.Services.AddSingleton<JobRunner>();

builder.Services.AddBoardDiscovery(builder.Configuration);

builder.Services.AddTelegramSink(builder.Configuration);

AddRoutines(builder.Services, role);

var host = builder.Build();

await PrepareStorage(host);

if (role is HostRole.All or HostRole.Bot)
{
    await host.RunAsync();
    return 0;
}

return await RunJob(host, role);

HostRole ParseRole(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
        return HostRole.All;

    if (Enum.TryParse<HostRole>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
        return parsed;

    throw new ArgumentException($"Unknown role '{value}', expected one of: {string.Join(", ", Enum.GetNames<HostRole>())}");
}

void AddRoutines(IServiceCollection services, HostRole hostRole)
{
    if (hostRole is HostRole.All or HostRole.Bot)
        services.AddHostedService<TelegramBotListener>();

    // The outbox is dispatched only next to the traversals: its cutoff reads the in-process progress tracker, so a
    // bot-side dispatcher would see every job traversal as idle and send each company on its own.
    if (hostRole != HostRole.All)
        return;

    services.AddHostedService<PollingWorker>();
    services.AddHostedService<RegistryPollingWorker>();
    services.AddHostedService<OutboxDispatcher>();
    services.AddHostedService<OutboxCleanupWorker>();
    services.AddHostedService<BoardDiscoveryWorker>();
}

async Task<int> RunJob(IHost h, HostRole hostRole)
{
    // Starting the host wires SIGINT/SIGTERM into ApplicationStopping - a cancelled workflow stops the job gracefully.
    await h.StartAsync();

    var lifetime = h.Services.GetRequiredService<IHostApplicationLifetime>();
    var exitCode = await h.Services.GetRequiredService<JobRunner>().RunAsync(hostRole, lifetime.ApplicationStopping);

    await h.StopAsync();

    ConsoleLog.Flush();
    FileLog.FlushAll();

    return exitCode;
}

async Task PrepareStorage(IHost h)
{
    await using var scope = h.Services.CreateAsyncScope();

    var db = scope.ServiceProvider
        .GetRequiredService<JobsPulseDbContext>();

    await db.Database.MigrateAsync();

    // Legacy `watchlist.json` is imported once, only into an empty installation.
    await LegacyWatchlistImporter.ImportAsync(
        scope.ServiceProvider.GetRequiredService<IWatchlistStorage>(),
        Path.Combine(AppContext.BaseDirectory, "watchlist.json"),
        scope.ServiceProvider.GetRequiredService<ILog>(),
        CancellationToken.None);
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