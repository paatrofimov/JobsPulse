using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model;
using Microsoft.Extensions.Logging;

namespace JobsPulse.Core.Services;

/// <summary>
/// Сценарий «добавить компанию по имени» — то, что стоит за командой бота.
/// Пользователь оперирует названиями, слаги остаются деталью реализации.
///
/// Порядок попыток (см. сценарии 1–4 в плане):
///   1. уже в watchlist            → сказать «уже слежу»
///   2. ссылка вместо имени        → разобрать карьерную страницу
///   3. поиск по имени у резолвера → показать кандидатов на выбор
///   4. ничего не нашли            → попросить ссылку
/// </summary>
public sealed class WatchService(
    IWatchlistProvider watchlist,
    ISourceCatalog sources,
    ILogger<WatchService> log)
{
    public async Task<LookupResult> LookupAsync(string query, CancellationToken ct)
    {
        query = query.Trim();
        if (query.Length == 0) return LookupResult.NotFound(query);

        var existing = watchlist.Current.Find(query);
        if (existing is not null) return LookupResult.AlreadyWatched(existing);

        var isUrl = query.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    || query.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

        var candidates = new List<BoardCandidate>();

        foreach (var sourceId in sources.SourceIds)
        {
            var resolver = sources.GetResolver(sourceId);
            if (resolver is null) continue;

            try
            {
                if (isUrl)
                {
                    var single = await resolver.ResolveByUrlAsync(query, ct);
                    if (single is not null) candidates.Add(single);
                }
                else
                {
                    candidates.AddRange(await resolver.ResolveByNameAsync(query, ct));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogWarning(ex, "Резолвер {Source} упал на запросе '{Query}'", sourceId, query);
            }
        }

        // Уже отслеживаемые борды из выдачи убираем — иначе пользователь добавит дубль.
        var known = watchlist.Current.Entries
            .Select(e => $"{e.Source}/{e.Board}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var fresh = candidates
            .Where(c => !known.Contains($"{c.SourceId}/{c.BoardKey}"))
            .OrderByDescending(c => c.Resolution == ResolutionKind.DirectSlug)
            .ThenByDescending(c => c.JobCount)
            .Take(5)
            .ToList();

        return fresh.Count == 0 ? LookupResult.NotFound(query) : LookupResult.Found(query, fresh);
    }

    /// <summary>Подтверждённое пользователем добавление. Запись создаётся незасеянной — первый цикл пройдёт молча.</summary>
    public async Task<WatchEntry> AddAsync(BoardCandidate candidate, FilterSpec? filter, CancellationToken ct)
    {
        var entry = new WatchEntry
        {
            Id = $"{candidate.SourceId}:{candidate.BoardKey}",
            Source = candidate.SourceId,
            Board = candidate.BoardKey,
            CompanyName = candidate.DisplayName,
            Enabled = true,
            Filter = filter,
            SeededAt = null
        };

        var added = await watchlist.AddAsync(entry, ct);
        log.LogInformation("Добавлена компания {Company} ({Source}/{Board})",
            added.CompanyName, added.Source, added.Board);

        return added;
    }

    public Task<bool> RemoveAsync(string idOrName, CancellationToken ct)
    {
        var entry = watchlist.Current.Find(idOrName);
        return entry is null ? Task.FromResult(false) : watchlist.RemoveAsync(entry.Id, ct);
    }

    public IReadOnlyList<WatchEntry> List() => watchlist.Current.Entries;
}

public sealed record LookupResult
{
    public required LookupStatus Status { get; init; }
    public required string Query { get; init; }
    public IReadOnlyList<BoardCandidate> Candidates { get; init; } = [];
    public WatchEntry? Existing { get; init; }

    public static LookupResult Found(string query, IReadOnlyList<BoardCandidate> candidates) =>
        new() { Status = LookupStatus.Found, Query = query, Candidates = candidates };

    public static LookupResult NotFound(string query) =>
        new() { Status = LookupStatus.NotFound, Query = query };

    public static LookupResult AlreadyWatched(WatchEntry entry) =>
        new() { Status = LookupStatus.AlreadyWatched, Query = entry.CompanyName, Existing = entry };
}

public enum LookupStatus
{
    Found,
    NotFound,
    AlreadyWatched
}
