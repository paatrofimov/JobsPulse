namespace JobsPulse.Sources.Greenhouse;

public sealed class GreenhouseOptions
{
    public const string SectionName = "Sources:Greenhouse";

    /// <summary>Публичный Job Board API. Кредов не требует — все GET открыты.</summary>
    public string BaseUrl { get; set; } = "https://boards-api.greenhouse.io/v1/boards/";

    /// <summary>
    /// Тянуть ли описания вакансий в цикле поллинга.
    /// По умолчанию нет: content=true раздувает ответ в разы, а для матчинга по заголовку и локации не нужен.
    /// </summary>
    public bool IncludeContentOnPoll { get; set; }

    /// <summary>Сколько вариантов слага перебирать при добавлении компании по имени.</summary>
    public int MaxSlugGuesses { get; set; } = 8;

    /// <summary>Отдаётся в User-Agent — вежливость и возможность для Greenhouse связаться при проблемах.</summary>
    public string ContactInfo { get; set; } = "jobs-pulse-job-watcher (personal project)";
}
