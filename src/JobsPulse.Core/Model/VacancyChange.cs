namespace JobsPulse.Core.Model;

public enum ChangeKind
{
    /// <summary>Вакансии не было в хранилище — первое появление.</summary>
    New,

    /// <summary>Вакансия была, но контент-хеш изменился.</summary>
    Updated,

    /// <summary>
    /// Вакансия была в хранилище, но не пришла в текущем полном фетче.
    /// Выставляется ТОЛЬКО если фетч завершился успешно и полностью (см. SourceFetchResult.IsComplete),
    /// иначе одна сетевая ошибка «закроет» весь борд разом.
    /// </summary>
    Closed
}

public sealed record VacancyChange
{
    public required ChangeKind Kind { get; init; }

    /// <summary>Текущее состояние вакансии. Для Closed — последнее известное.</summary>
    public required Vacancy Vacancy { get; init; }

    /// <summary>Id записи watchlist, из-за которой вакансия попала в выборку.</summary>
    public required string WatchEntryId { get; init; }

    /// <summary>Человекочитаемое имя компании (из watchlist / каталога), для сообщения.</summary>
    public required string CompanyName { get; init; }

    public required string ContentHash { get; init; }
}
