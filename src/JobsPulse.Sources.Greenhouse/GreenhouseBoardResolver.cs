using System.Text.RegularExpressions;
using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobsPulse.Sources.Greenhouse;

/// <summary>
/// Поиск борда по названию компании. Реализует сценарии 2–3 из плана:
/// сначала пробуем угадать слаг, если не вышло — просим ссылку на карьерную страницу
/// и вытаскиваем слаг оттуда.
///
/// Каждый найденный кандидат проверяется реальным запросом, поэтому пользователю
/// показываются только живые борды — с их настоящим названием и числом вакансий.
/// </summary>
public sealed partial class GreenhouseBoardResolver(
    GreenhouseBoardClient client,
    IHttpClientFactory httpFactory,
    IOptions<GreenhouseOptions> options,
    ILogger<GreenhouseBoardResolver> log) : IBoardResolver
{
    public string SourceId => GreenhouseMapper.SourceId;

    public async Task<IReadOnlyList<BoardCandidate>> ResolveByNameAsync(string companyName, CancellationToken ct)
    {
        var guesses = SlugGuesser.Generate(companyName, options.Value.MaxSlugGuesses);
        var found = new List<BoardCandidate>();

        // Последовательно, а не параллельно: это интерактивный сценарий, и восемь
        // одновременных запросов по угадываемым слагам выглядят как скан.
        for (var i = 0; i < guesses.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var candidate = await ProbeAsync(guesses[i], ct);
            if (candidate is null) continue;

            found.Add(candidate with
            {
                Resolution = i == 0 ? ResolutionKind.DirectSlug : ResolutionKind.Guessed
            });

            // Первое же точное попадание почти всегда верное — дальше не мучаем API.
            if (i == 0) break;
        }

        return found;
    }

    public async Task<BoardCandidate?> ResolveByUrlAsync(string url, CancellationToken ct)
    {
        // Прямая ссылка на борд Greenhouse.
        var direct = SlugGuesser.ExtractFromUrl(url);
        if (direct is not null) return await ProbeAsync(direct, ct);

        // Иначе это карьерная страница компании: качаем и ищем в ней встроенный борд.
        try
        {
            using var http = httpFactory.CreateClient(GreenhouseBoardClient.HttpClientName);
            using var response = await http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return null;

            var html = await response.Content.ReadAsStringAsync(ct);

            foreach (Match match in BoardUrlPattern().Matches(html))
            {
                var slug = match.Groups["slug"].Value;
                if (string.IsNullOrWhiteSpace(slug)) continue;

                var candidate = await ProbeAsync(slug, ct);
                if (candidate is not null) return candidate with { Resolution = ResolutionKind.CareersPage };
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "Не удалось разобрать карьерную страницу {Url}", url);
        }

        return null;
    }

    public async Task<BoardCandidate?> ProbeAsync(string boardKey, CancellationToken ct)
    {
        var board = await client.GetBoardAsync(boardKey, ct);
        if (!board.Success) return null;

        var jobs = await client.GetJobsAsync(boardKey, includeContent: false, ct);
        if (!jobs.Success) return null;

        return new BoardCandidate
        {
            SourceId = SourceId,
            BoardKey = boardKey,
            // Имя берём с самого борда — именно оно показывается пользователю вместо слага.
            DisplayName = string.IsNullOrWhiteSpace(board.Value!.Name) ? boardKey : board.Value.Name!.Trim(),
            JobCount = jobs.Value!.Meta?.Total ?? jobs.Value.Jobs.Count,
            BoardUrl = $"https://job-boards.greenhouse.io/{boardKey}"
        };
    }

    [GeneratedRegex(
        @"(?:boards|job-boards)\.greenhouse\.io/(?:embed/job_board\?for=)?(?<slug>[a-z0-9_-]+)",
        RegexOptions.IgnoreCase)]
    private static partial Regex BoardUrlPattern();
}
