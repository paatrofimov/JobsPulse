namespace JobsPulse.Core.Model;

public enum MatchMode
{
    /// <summary>Подстрока, без учёта регистра. Дефолт.</summary>
    Contains,

    /// <summary>Полное совпадение, без учёта регистра.</summary>
    Exact,

    /// <summary>Регулярка. Выполняется с NonBacktracking и таймаутом — кривой шаблон не должен вешать воркер.</summary>
    Regex
}

/// <summary>
/// Критерии отбора вакансий.
/// Семантика: внутри одного списка — ИЛИ, между разными полями — И, *NoneOf — жёсткое исключение.
/// Пустой список = «условие не задано», а не «ничего не подходит».
/// </summary>
public sealed record FilterSpec
{
    public IReadOnlyList<string> TitleAnyOf { get; init; } = [];
    public IReadOnlyList<string> TitleNoneOf { get; init; } = [];
    public IReadOnlyList<string> LocationAnyOf { get; init; } = [];
    public IReadOnlyList<string> LocationNoneOf { get; init; } = [];
    public IReadOnlyList<string> DepartmentAnyOf { get; init; } = [];

    /// <summary>Отсечь вакансии, опубликованные раньше N дней назад. null = без ограничения.</summary>
    public int? PostedWithinDays { get; init; }

    public MatchMode MatchMode { get; init; } = MatchMode.Contains;

    public static readonly FilterSpec MatchAll = new();

    public bool IsEmpty =>
        TitleAnyOf.Count == 0 && TitleNoneOf.Count == 0 &&
        LocationAnyOf.Count == 0 && LocationNoneOf.Count == 0 &&
        DepartmentAnyOf.Count == 0 && PostedWithinDays is null;
}
