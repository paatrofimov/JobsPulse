using JobsPulse.Core.Model;

namespace JobsPulse.Core.Abstractions;

/// <summary>
/// Источник конфигурации мониторинга с поддержкой горячей замены.
///
/// Этап 1 — файловая реализация (watchlist.json + IOptionsMonitor + запись обратно из бота).
/// Этап 2 — та же самая абстракция поверх БД, ядро и бот не меняются.
///
/// Контракт: <see cref="Current"/> ВСЕГДА возвращает последнюю ВАЛИДНУЮ версию.
/// Если файл сохранили битым — провайдер логирует ошибку и продолжает отдавать предыдущую.
/// </summary>
public interface IWatchlistProvider
{
    Watchlist Current { get; }

    Task<WatchEntry> AddAsync(WatchEntry entry, CancellationToken ct);

    Task<bool> RemoveAsync(string entryId, CancellationToken ct);

    Task<bool> SetEnabledAsync(string entryId, bool enabled, CancellationToken ct);

    /// <summary>Отметить запись засеянной: с этого момента её изменения идут в уведомления.</summary>
    Task MarkSeededAsync(string entryId, string filterHash, CancellationToken ct);

    /// <summary>Подписка на изменение конфигурации (файл поправили руками / бот добавил компанию).</summary>
    IDisposable OnChange(Action<Watchlist> listener);
}
