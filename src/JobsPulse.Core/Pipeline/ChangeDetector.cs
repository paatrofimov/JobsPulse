using JobsPulse.Core.Model.Domain;
using JobsPulse.Core.Model.Infrastructure;

namespace JobsPulse.Core.Pipeline;

public sealed class ChangeDetector
{
    public sealed record Input
    {
        public required WatchEntry Entry { get; init; }
        public required SourceTraverseResult Traverse { get; init; }

        public required IReadOnlyList<Vacancy> Matched { get; init; }

        public required IReadOnlyDictionary<string, Vacancy> Seen { get; init; }
    }

    public sealed record Output
    {
        public required IReadOnlyList<VacancyChange> VacanciesChanges { get; init; }
        public required IReadOnlyList<Vacancy> VacanciesUpserts { get; init; }
        public required IReadOnlyList<string> ClosedPostIds { get; init; }
    }

    public Output Detect(Input input)
    {
        var changes = new List<VacancyChange>();
        var upserts = new List<Vacancy>(input.Matched.Count);

        // Single vacancy can be duplicated in many posts (locations/langiages).
        // Deduplicate by (GroupId, Location).
        foreach (var vacancy in Deduplicate(input.Matched))
        {
            var hash = VacancyHasher.Compute(vacancy);
            upserts.Add(vacancy);

            if (!input.Seen.TryGetValue(vacancy.PostId, out var seen))
            {
                changes.Add(Change(VacancyChangeKind.New, vacancy, hash, input.Entry));
            }
            else if (!string.Equals(seen.ContentHash, hash, StringComparison.Ordinal))
            {
                changes.Add(Change(VacancyChangeKind.Updated, vacancy, hash, input.Entry));
            }
        }

        // Closed can be set only if complete board was traversed.
        // Otherwise can false-positively decide that unfetched vacancy is closed.
        var closed = new List<string>();
        if (input.Traverse.IsComplete)
        {
            var present = upserts.Select(v => v.PostId).ToHashSet(StringComparer.Ordinal);

            foreach (var (postId, seen) in input.Seen)
            {
                if (present.Contains(postId))
                    continue;

                closed.Add(postId);

                changes.Add(new VacancyChange
                {
                    Kind = VacancyChangeKind.Closed,
                    WatchEntryId = input.Entry.Id,
                    CompanyName = input.Entry.CompanyName,
                    ContentHash = seen.ContentHash,
                    Vacancy = new Vacancy
                    {
                        SourceId = input.Entry.VacancySourceId,
                        BoardId = input.Entry.BoardId,
                        PostId = postId,
                        Title = seen.Title,
                        Location = seen.Location,
                        Url = seen.Url,
                        UpdatedAt = seen.UpdatedAt,
                        ContentHash = seen.ContentHash
                    }
                });
            }
        }

        return new Output { VacanciesChanges = changes, VacanciesUpserts = upserts, ClosedPostIds = closed };
    }

    private static IEnumerable<Vacancy> Deduplicate(IReadOnlyList<Vacancy> vacancies)
    {
        var seenGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var v in vacancies)
        {
            // prospect-post, skipping
            if (string.IsNullOrEmpty(v.GroupId))
            {
                yield return v;
                continue;
            }

            if (seenGroups.Add($"{v.GroupId}|{v.Location}"))
                yield return v;
        }
    }

    private static VacancyChange Change(VacancyChangeKind kind, Vacancy v, string hash, WatchEntry entry) =>
        new()
        {
            Kind = kind,
            Vacancy = v,
            ContentHash = hash,
            WatchEntryId = entry.Id,
            CompanyName = entry.CompanyName
        };
}