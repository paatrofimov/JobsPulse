namespace JobsPulse.Core.Model;

/// <summary>
/// Нормализованная вакансия — общий знаменатель для всех ATS (Greenhouse, Lever, Ashby, ...).
/// Всё, что специфично для конкретного источника, уходит в <see cref="Extra"/>.
/// </summary>
public sealed record Vacancy
{
    /// <summary>Идентификатор источника: "greenhouse", "lever", ...</summary>
    public required string SourceId { get; init; }

    /// <summary>Ключ борда внутри источника (для Greenhouse — board_token).</summary>
    public required string BoardKey { get; init; }

    /// <summary>Идентификатор поста вакансии в источнике. Уникален в пределах (SourceId, BoardKey).</summary>
    public required string ExternalId { get; init; }

    /// <summary>
    /// Идентификатор «вакансии» как сущности выше поста (Greenhouse internal_job_id).
    /// Одна вакансия может быть опубликована несколькими постами (разные локации/языки).
    /// Используется для схлопывания дублей. Может быть null.
    /// </summary>
    public string? GroupId { get; init; }

    public required string Title { get; init; }

    public string? Location { get; init; }

    public IReadOnlyList<string> Departments { get; init; } = [];

    public IReadOnlyList<string> Offices { get; init; } = [];

    public required string Url { get; init; }

    /// <summary>Момент последнего изменения по данным источника. Ненадёжен как признак «новизны» — см. ContentHash.</summary>
    public DateTimeOffset UpdatedAt { get; init; }

    public DateTimeOffset? FirstPublished { get; init; }

    /// <summary>HTML описания. Заполняется только при детальном фетче, в списочном режиме null.</summary>
    public string? DescriptionHtml { get; init; }

    /// <summary>Произвольные поля источника: custom fields, вилки, requisition_id.</summary>
    public IReadOnlyDictionary<string, string?> Extra { get; init; } =
        new Dictionary<string, string?>();

    /// <summary>Составной ключ строки в хранилище.</summary>
    public VacancyKey Key => new(SourceId, BoardKey, ExternalId);
}

public readonly record struct VacancyKey(string SourceId, string BoardKey, string ExternalId)
{
    public override string ToString() => $"{SourceId}/{BoardKey}/{ExternalId}";
}
