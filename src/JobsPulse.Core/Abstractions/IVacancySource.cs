using JobsPulse.Core.Model;

namespace JobsPulse.Core.Abstractions;

/// <summary>
/// Поставщик вакансий. Одна реализация = один ATS.
/// Регистрируется в DI как keyed-сервис по <see cref="SourceId"/>, оркестратор резолвит по WatchEntry.Source.
/// Добавление Lever/Ashby/Workable = новый проект + строка регистрации; ядро не трогается.
/// </summary>
public interface IVacancySource
{
    string SourceId { get; }

    /// <summary>
    /// Обойти один борд целиком. Реализация обязана честно выставить IsComplete:
    /// частичный результат, помеченный как полный, приведёт к ложным «вакансия закрыта».
    /// </summary>
    Task<SourceFetchResult> FetchAsync(SourceTarget target, CancellationToken ct);
}

/// <summary>
/// Поиск борда по человекочитаемому имени компании — то, что стоит за командой бота «/watch Finom».
/// Пользователь не должен знать про слаги.
/// </summary>
public interface IBoardResolver
{
    string SourceId { get; }

    /// <summary>Найти кандидатов по имени компании. Возвращает то, что реально отвечает и имеет вакансии.</summary>
    Task<IReadOnlyList<BoardCandidate>> ResolveByNameAsync(string companyName, CancellationToken ct);

    /// <summary>Достать борд из ссылки на карьерную страницу — запасной путь, когда угадать не вышло.</summary>
    Task<BoardCandidate?> ResolveByUrlAsync(string url, CancellationToken ct);

    /// <summary>Проверить конкретный ключ борда: жив ли, как называется, сколько вакансий.</summary>
    Task<BoardCandidate?> ProbeAsync(string boardKey, CancellationToken ct);
}
