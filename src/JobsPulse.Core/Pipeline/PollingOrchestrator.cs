using System.Threading.RateLimiting;
using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model;
using JobsPulse.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobsPulse.Core.Pipeline;

/// <summary>
/// Ядро системы. Один «цикл» = обход всех активных записей watchlist.
///
/// Поток данных:
///   watchlist → источник → фильтр → детектор изменений → (состояние + outbox одной транзакцией)
///
/// Оркестратор НЕ отправляет сообщения. Он только кладёт их в outbox.
/// Отправкой занимается отдельный диспетчер — так падение Telegram не теряет уведомления.
/// </summary>
public sealed class PollingOrchestrator(
    IWatchlistProvider watchlist,
    ISourceCatalog sources,
    IStateStore state,
    VacancyMatcher matcher,
    ChangeDetector detector,
    IOptionsMonitor<PollingOptions> options,
    TimeProvider clock,
    ILogger<PollingOrchestrator> log)
{
    private readonly Dictionary<string, DateTimeOffset> _lastRunByEntry = new(StringComparer.OrdinalIgnoreCase);

    public async Task<CycleReport> RunCycleAsync(CancellationToken ct)
    {
        var opts = options.CurrentValue;
        var current = watchlist.Current;
        var now = clock.GetUtcNow();

        var due = current.Entries.Where(e => e.Enabled && IsDue(e, opts, now)).ToList();

        if (due.Count == 0)
        {
            log.LogDebug("Нет записей к обходу (всего в watchlist: {Total})", current.Entries.Count);
            return CycleReport.Empty;
        }

        log.LogInformation("Цикл: {Due} из {Total} записей к обходу", due.Count, current.Entries.Count);

        using var limiter = CreateRateLimiter(opts);
        using var gate = new SemaphoreSlim(opts.MaxConcurrency);

        var results = await Task.WhenAll(due.Select(async entry =>
        {
            await gate.WaitAsync(ct);
            try
            {
                using var lease = await limiter.AcquireAsync(1, ct);
                return await ProcessEntryAsync(entry, current, opts, ct);
            }
            finally
            {
                gate.Release();
            }
        }));

        foreach (var entry in due) _lastRunByEntry[entry.Id] = now;

        var report = CycleReport.Aggregate(results);
        log.LogInformation(
            "Цикл завершён: бордов {Boards}, вакансий {Fetched}, подошло {Matched}, изменений {Changes}, ошибок {Failed}",
            report.BoardsProcessed, report.VacanciesFetched, report.VacanciesMatched, report.Changes, report.Failed);

        return report;
    }

    private async Task<EntryReport> ProcessEntryAsync(
        WatchEntry entry, Watchlist config, PollingOptions opts, CancellationToken ct)
    {
        var source = sources.GetSource(entry.Source);
        if (source is null)
        {
            log.LogWarning("Источник '{Source}' не зарегистрирован — запись {Entry} пропущена", entry.Source, entry.Id);
            return EntryReport.Failure();
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(opts.BoardTimeoutSeconds));

        SourceFetchResult fetch;
        try
        {
            fetch = await source.FetchAsync(
                new SourceTarget { SourceId = entry.Source, BoardKey = entry.Board },
                timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            log.LogWarning("Таймаут обхода борда {Company} ({Board})", entry.CompanyName, entry.Board);
            return EntryReport.Failure();
        }

        if (fetch.BoardMissing)
        {
            // Борд не существует — ретраить бессмысленно, гасим запись, чтобы не долбить 404.
            log.LogWarning("Борд {Board} ({Company}) не найден — отключаю запись", entry.Board, entry.CompanyName);
            await watchlist.SetEnabledAsync(entry.Id, false, ct);
            return EntryReport.Failure();
        }

        if (!fetch.IsComplete)
        {
            log.LogWarning("Неполный обход {Company}: {Error}. Изменения не применяются", entry.CompanyName, fetch.Error);
            return EntryReport.Failure();
        }

        var filter = entry.Filter ?? config.DefaultFilter;
        var matched = matcher.Apply(fetch.Vacancies, filter);
        var seen = await state.LoadSeenAsync(entry.Source, entry.Board, ct);

        var detected = detector.Detect(new ChangeDetector.Input
        {
            Entry = entry,
            Fetch = fetch,
            Matched = matched,
            Seen = seen
        });

        // Засев: первый проход (или проход после смены фильтра) пишет состояние, но молчит.
        // Иначе добавление компании = вся её доска вакансий в чат одним залпом.
        var filterHash = VacancyHasher.ComputeFilterHash(filter);
        var needsSeeding = entry.SeededAt is null || entry.SeededFilterHash != filterHash;

        var notifications = needsSeeding || opts.DryRun
            ? []
            : BuildNotifications(detected.Changes, entry, config);

        await state.CommitAsync(new StateCommit
        {
            SourceId = entry.Source,
            BoardKey = entry.Board,
            Upserts = detected.Upserts,
            Closed = detected.Closed,
            Notifications = notifications
        }, ct);

        if (needsSeeding)
        {
            await watchlist.MarkSeededAsync(entry.Id, filterHash, ct);
            log.LogInformation(
                "Засеяно {Company}: {Count} вакансий записано без уведомлений",
                entry.CompanyName, detected.Upserts.Count);
        }
        else if (opts.DryRun && detected.Changes.Count > 0)
        {
            log.LogInformation(
                "DRY-RUN {Company}: улетело бы {Count} уведомлений ({New} новых)",
                entry.CompanyName, detected.Changes.Count,
                detected.Changes.Count(c => c.Kind == ChangeKind.New));
        }

        return new EntryReport(
            Fetched: fetch.Vacancies.Count,
            Matched: matched.Count,
            Changes: detected.Changes.Count,
            Failed: false);
    }

    private static IReadOnlyList<OutboxItem> BuildNotifications(
        IReadOnlyList<VacancyChange> changes, WatchEntry entry, Watchlist config)
    {
        var target = entry.Delivery ?? config.DefaultDelivery;

        // Пустой ChatId — это «доставка не настроена», а не «отправить в чат с пустым id».
        if (target is null || string.IsNullOrWhiteSpace(target.ChatId) || changes.Count == 0) return [];

        return changes.Select(c => new OutboxItem
        {
            // Ключ идемпотентности: одно и то же изменение не встанет в очередь дважды.
            DedupKey = $"{c.Vacancy.Key}|{c.Kind}|{c.ContentHash}",
            ChatId = target.ChatId,
            Silent = target.Silent,
            Kind = c.Kind,
            CompanyName = c.CompanyName,
            Vacancy = c.Vacancy
        }).ToList();
    }

    private bool IsDue(WatchEntry entry, PollingOptions opts, DateTimeOffset now)
    {
        var interval = TimeSpan.FromMinutes(entry.IntervalMinutesOverride ?? opts.IntervalMinutes);
        return !_lastRunByEntry.TryGetValue(entry.Id, out var last) || now - last >= interval;
    }

    private static RateLimiter CreateRateLimiter(PollingOptions opts)
    {
        var perSecond = Math.Max(1, (int)Math.Ceiling(opts.MaxRequestsPerSecond));
        return new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = perSecond,
            TokensPerPeriod = perSecond,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            QueueLimit = int.MaxValue,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true
        });
    }
}

public readonly record struct EntryReport(int Fetched, int Matched, int Changes, bool Failed)
{
    public static EntryReport Failure() => new(0, 0, 0, true);
}

public readonly record struct CycleReport(
    int BoardsProcessed, int VacanciesFetched, int VacanciesMatched, int Changes, int Failed)
{
    public static readonly CycleReport Empty = new(0, 0, 0, 0, 0);

    public static CycleReport Aggregate(IReadOnlyList<EntryReport> entries) => new(
        entries.Count,
        entries.Sum(e => e.Fetched),
        entries.Sum(e => e.Matched),
        entries.Sum(e => e.Changes),
        entries.Count(e => e.Failed));
}
