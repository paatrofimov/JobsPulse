using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Options;
using JobsPulse.Core.Pipeline;
using JobsPulse.Core.Services;
using JobsPulse.Host.Infrastructure;
using JobsPulse.Host.Workers;
using JobsPulse.Sinks.Telegram;
using JobsPulse.Sources.Greenhouse;
using JobsPulse.Storage;

var builder = Host.CreateApplicationBuilder(args);

// watchlist отдельным файлом: его правят чаще остальных настроек, и правит его в том числе бот.
builder.Configuration.AddJsonFile("watchlist.json", optional: true, reloadOnChange: false);

// Секреты: локально — user-secrets, в проде — переменные окружения (Telegram__BotToken).
// В appsettings.json токенов быть не должно.
builder.Configuration.AddEnvironmentVariables();

builder.Services.AddSingleton(TimeProvider.System);

builder.Services.Configure<PollingOptions>(builder.Configuration.GetSection(PollingOptions.SectionName));
builder.Services.Configure<DeliveryOptions>(builder.Configuration.GetSection(DeliveryOptions.SectionName));

// --- Источники (плагины). Следующий ATS добавляется одной строкой здесь. ---
builder.Services.AddGreenhouseSource(builder.Configuration);

var registeredSources = new[] { GreenhouseMapper.SourceId };
builder.Services.AddSingleton<ISourceCatalog>(sp => new SourceCatalog(sp, registeredSources));

// --- Хранилище: состояние + outbox в одной БД, чтобы писать их одной транзакцией. ---
builder.Services.AddSqliteStorage(builder.Configuration);

// --- Конфигурация мониторинга с горячей заменой. ---
builder.Services.AddSingleton<IWatchlistProvider>(sp => new FileWatchlistProvider(
    Path.Combine(AppContext.BaseDirectory, "watchlist.json"),
    sp.GetRequiredService<TimeProvider>(),
    sp.GetRequiredService<ILogger<FileWatchlistProvider>>()));

// --- Ядро конвейера. ---
builder.Services.AddSingleton<VacancyMatcher>();
builder.Services.AddSingleton<ChangeDetector>();
builder.Services.AddSingleton<PollingOrchestrator>();
builder.Services.AddSingleton<WatchService>();

// --- Доставка + бот. ---
builder.Services.AddTelegramSink(builder.Configuration);

// --- Фоновые процессы. ---
builder.Services.AddHostedService<PollingWorker>();
builder.Services.AddHostedService<OutboxDispatcher>();

var host = builder.Build();
host.Run();
