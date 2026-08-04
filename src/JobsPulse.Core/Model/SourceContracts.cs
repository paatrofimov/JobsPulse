namespace JobsPulse.Core.Model;

/// <summary>Что именно опрашиваем у источника.</summary>
public sealed record SourceTarget
{
    public required string SourceId { get; init; }
    public required string BoardKey { get; init; }

    /// <summary>
    /// Подсказка источнику: нужно ли тянуть описания.
    /// Для Greenhouse описания резко утяжеляют ответ, поэтому в цикле поллинга — false.
    /// </summary>
    public bool IncludeDescriptions { get; init; }
}

/// <summary>
/// Результат обхода одного борда.
/// <see cref="IsComplete"/> — ключевое поле: без него нельзя безопасно определять закрытые вакансии.
/// </summary>
public sealed record SourceFetchResult
{
    public required bool IsComplete { get; init; }
    public required IReadOnlyList<Vacancy> Vacancies { get; init; }
    public string? Error { get; init; }

    /// <summary>Борд не существует (404). Повторять бессмысленно — запись стоит отключить.</summary>
    public bool BoardMissing { get; init; }

    public static SourceFetchResult Complete(IReadOnlyList<Vacancy> vacancies) =>
        new() { IsComplete = true, Vacancies = vacancies };

    public static SourceFetchResult Failed(string error, bool boardMissing = false) =>
        new() { IsComplete = false, Vacancies = [], Error = error, BoardMissing = boardMissing };
}

/// <summary>Кандидат на добавление, который бот показывает пользователю при поиске по имени.</summary>
public sealed record BoardCandidate
{
    public required string SourceId { get; init; }
    public required string BoardKey { get; init; }

    /// <summary>Название компании, как его отдаёт сам борд. Именно это видит пользователь.</summary>
    public required string DisplayName { get; init; }

    public int JobCount { get; init; }
    public string? BoardUrl { get; init; }

    /// <summary>Как нашли: точное совпадение слага, угадывание, разбор карьерной страницы.</summary>
    public ResolutionKind Resolution { get; init; }
}

public enum ResolutionKind
{
    /// <summary>Слаг совпал напрямую с нормализованным именем.</summary>
    DirectSlug,

    /// <summary>Слаг угадан перебором коротких вариантов.</summary>
    Guessed,

    /// <summary>Слаг извлечён из карьерной страницы, которую дал пользователь.</summary>
    CareersPage,

    /// <summary>Взято из локального каталога (появится на этапе реестра).</summary>
    Catalog
}
