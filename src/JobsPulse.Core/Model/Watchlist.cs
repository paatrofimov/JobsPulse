namespace JobsPulse.Core.Model;

/// <summary>
/// Конфигурация того, что мониторим. Меняется на горячую — см. IWatchlistProvider.
/// На этапе 1 наполняется руками (watchlist.json) и командами бота.
/// На этапе 2 сюда же приедет реестр бордов, и Entries станет «списком приоритетных», а не «списком вообще».
/// </summary>
public sealed record Watchlist
{
    /// <summary>Версия документа, растёт при каждой записи. Нужна для отладки горячей перезагрузки.</summary>
    public int Version { get; init; }

    /// <summary>Фильтр по умолчанию: применяется к записям, у которых нет своего.</summary>
    public FilterSpec DefaultFilter { get; init; } = new();

    /// <summary>Куда слать по умолчанию.</summary>
    public DeliveryTarget? DefaultDelivery { get; init; }

    public IReadOnlyList<WatchEntry> Entries { get; init; } = [];

    public WatchEntry? Find(string idOrName) =>
        Entries.FirstOrDefault(e =>
            string.Equals(e.Id, idOrName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(e.CompanyName, idOrName, StringComparison.OrdinalIgnoreCase));
}

public sealed record WatchEntry
{
    /// <summary>Стабильный идентификатор записи, напр. "greenhouse:finom".</summary>
    public required string Id { get; init; }

    public bool Enabled { get; init; } = true;

    /// <summary>Какой IVacancySource обслуживает запись.</summary>
    public required string Source { get; init; }

    /// <summary>Ключ борда в источнике (Greenhouse board_token).</summary>
    public required string Board { get; init; }

    /// <summary>Человекочитаемое имя компании — то, что видит пользователь. Слаг он не видит никогда.</summary>
    public required string CompanyName { get; init; }

    /// <summary>Переопределение периода опроса для этой записи.</summary>
    public int? IntervalMinutesOverride { get; init; }

    /// <summary>Персональный фильтр. Если null — берётся Watchlist.DefaultFilter.</summary>
    public FilterSpec? Filter { get; init; }

    public DeliveryTarget? Delivery { get; init; }

    /// <summary>
    /// Когда запись была «засеяна»: первый проход записывает текущее состояние борда в хранилище
    /// БЕЗ отправки уведомлений, иначе при добавлении компании прилетит сразу вся её доска.
    /// null = ещё не засеяна.
    /// </summary>
    public DateTimeOffset? SeededAt { get; init; }

    /// <summary>
    /// Хеш фильтра на момент засева. Если фильтр расширили, старые отсеянные вакансии
    /// внезапно станут «новыми» — при смене хеша запись пересевается молча.
    /// </summary>
    public string? SeededFilterHash { get; init; }
}

public sealed record DeliveryTarget
{
    public required string ChatId { get; init; }
    public bool Silent { get; init; }
}
