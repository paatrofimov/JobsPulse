using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobsPulse.Sources.Greenhouse;

/// <summary>
/// Реализация <see cref="IVacancySource"/> поверх публичного Job Board API.
/// Плагин: ядро о нём ничего не знает, связь только через SourceId в watchlist.
/// </summary>
public sealed class GreenhouseBoardSource(
    GreenhouseBoardClient client,
    IOptions<GreenhouseOptions> options,
    ILogger<GreenhouseBoardSource> log) : IVacancySource
{
    public string SourceId => GreenhouseMapper.SourceId;

    public async Task<SourceFetchResult> FetchAsync(SourceTarget target, CancellationToken ct)
    {
        var includeContent = target.IncludeDescriptions || options.Value.IncludeContentOnPoll;

        var fetch = await client.GetJobsAsync(target.BoardKey, includeContent, ct);

        if (fetch.NotFound)
            return SourceFetchResult.Failed("борд не найден", boardMissing: true);

        if (!fetch.Success)
            return SourceFetchResult.Failed(fetch.Error ?? "неизвестная ошибка");

        var vacancies = fetch.Value!.Jobs
            .Select(j => GreenhouseMapper.ToVacancy(j, target.BoardKey))
            .ToList();

        // meta.total — единственная проверка полноты, которую даёт API.
        // Расхождение означает, что мы получили не весь борд, и определять «закрытые» нельзя.
        var expected = fetch.Value.Meta?.Total;
        if (expected is { } total && total != vacancies.Count)
        {
            log.LogWarning("Борд {Board}: получено {Actual} из {Expected} — считаю обход неполным",
                target.BoardKey, vacancies.Count, total);
            return new SourceFetchResult
            {
                IsComplete = false,
                Vacancies = vacancies,
                Error = $"частичный ответ: {vacancies.Count}/{total}"
            };
        }

        return SourceFetchResult.Complete(vacancies);
    }
}
