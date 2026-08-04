using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model;

namespace JobsPulse.Core.Pipeline;

/// <summary>
/// Сверка «что было» и «что стало» по одному борду.
///
/// Здесь сосредоточены два самых опасных места всей системы:
///  1. Closed выставляется ТОЛЬКО при полном успешном фетче. Иначе таймаут = «все вакансии закрылись».
///  2. Хранится и сравнивается только то, что прошло фильтр. Значит, вакансия, переставшая
///     подходить под фильтр, выглядит как Closed — это осознанный компромисс: пользователю
///     важно «оно больше не в моей выборке», а не «оно физически удалено».
/// </summary>
public sealed class ChangeDetector
{
    public sealed record Input
    {
        public required WatchEntry Entry { get; init; }
        public required SourceFetchResult Fetch { get; init; }

        /// <summary>Вакансии, прошедшие фильтр записи.</summary>
        public required IReadOnlyList<Vacancy> Matched { get; init; }

        public required IReadOnlyDictionary<string, SeenVacancy> Seen { get; init; }
    }

    public sealed record Output
    {
        public required IReadOnlyList<VacancyChange> Changes { get; init; }
        public required IReadOnlyList<Vacancy> Upserts { get; init; }
        public required IReadOnlyList<string> Closed { get; init; }
    }

    public Output Detect(Input input)
    {
        var changes = new List<VacancyChange>();
        var upserts = new List<Vacancy>(input.Matched.Count);

        // Схлопывание дублей: у Greenhouse одна вакансия может быть несколькими постами
        // (разные локации/языки). Берём по одному посту на (GroupId, Location).
        foreach (var vacancy in Deduplicate(input.Matched))
        {
            var hash = VacancyHasher.Compute(vacancy);
            upserts.Add(vacancy);

            if (!input.Seen.TryGetValue(vacancy.ExternalId, out var seen))
            {
                changes.Add(Change(ChangeKind.New, vacancy, hash, input.Entry));
            }
            else if (!string.Equals(seen.ContentHash, hash, StringComparison.Ordinal))
            {
                changes.Add(Change(ChangeKind.Updated, vacancy, hash, input.Entry));
            }
        }

        // Закрытые определяем только если уверены, что видели борд целиком.
        var closed = new List<string>();
        if (input.Fetch.IsComplete)
        {
            var present = upserts.Select(v => v.ExternalId).ToHashSet(StringComparer.Ordinal);

            foreach (var (externalId, seen) in input.Seen)
            {
                if (present.Contains(externalId)) continue;

                closed.Add(externalId);
                changes.Add(new VacancyChange
                {
                    Kind = ChangeKind.Closed,
                    WatchEntryId = input.Entry.Id,
                    CompanyName = input.Entry.CompanyName,
                    ContentHash = seen.ContentHash,
                    Vacancy = new Vacancy
                    {
                        SourceId = input.Entry.Source,
                        BoardKey = input.Entry.Board,
                        ExternalId = externalId,
                        Title = seen.Title,
                        Location = seen.Location,
                        Url = seen.Url,
                        UpdatedAt = seen.UpdatedAt
                    }
                });
            }
        }

        return new Output { Changes = changes, Upserts = upserts, Closed = closed };
    }

    private static IEnumerable<Vacancy> Deduplicate(IReadOnlyList<Vacancy> vacancies)
    {
        var seenGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var v in vacancies)
        {
            // Нет GroupId (prospect-пост) — дедупить не по чему, пропускаем как есть.
            if (string.IsNullOrEmpty(v.GroupId))
            {
                yield return v;
                continue;
            }

            if (seenGroups.Add($"{v.GroupId}|{v.Location}"))
                yield return v;
        }
    }

    private static VacancyChange Change(ChangeKind kind, Vacancy v, string hash, WatchEntry entry) =>
        new()
        {
            Kind = kind,
            Vacancy = v,
            ContentHash = hash,
            WatchEntryId = entry.Id,
            CompanyName = entry.CompanyName
        };
}
