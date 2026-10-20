using JobsPulse.Core.Model.Domain;
using JobsPulse.Sinks.Telegram.Infrastructure.Localization;
using JobsPulse.Core.Model.Infrastructure;

namespace JobsPulse.Sinks.Telegram.Infrastructure;

/// <summary>
/// One job posted for several cities is one line: the same board and the same title, whatever the post ids. The
/// title links to the first posting, and every city links to its own.
/// </summary>
public static class SameJob
{
    /// <summary>Groups in the order their first item comes in, so a sorted listing stays sorted.</summary>
    public static List<List<T>> Group<T>(IEnumerable<T> items, Func<T, Vacancy> vacancy, Func<T, object?>? extraKey = null)
    {
        var groups = new List<List<T>>();
        var index = new Dictionary<(string, string, string, object?), List<T>>();

        foreach (var item in items)
        {
            var v = vacancy(item);
            var key = (v.SourceId.ToLowerInvariant(), v.BoardId.ToLowerInvariant(), v.Title.Trim().ToLowerInvariant(),
                extraKey?.Invoke(item));

            if (!index.TryGetValue(key, out var group))
            {
                group = [];
                index[key] = group;
                groups.Add(group);
            }

            group.Add(item);
        }

        return groups;
    }

    /// <summary>
    /// The place of one posting as text, or of several as links - a city is the only thing telling the postings of
    /// one job apart, so it is what the reader clicks.
    /// </summary>
    public static string Places(IReadOnlyList<Vacancy> postings, BotLanguage language)
    {
        if (postings.Count == 1)
            return MessageFormatter.Escape(Place(postings[0]) ?? BotTexts.Get(TextKey.VacancyUnknownLocation, language));

        return string.Join(" · ", postings
            .Select(p => (Place: Place(p) ?? BotTexts.Get(TextKey.VacancyUnknownLocation, language), p.Url))
            .DistinctBy(p => p.Place, StringComparer.OrdinalIgnoreCase)
            .Select(p => $"<a href=\"{MessageFormatter.Escape(p.Url)}\">{MessageFormatter.Escape(p.Place)}</a>"));
    }

    private static string? Place(Vacancy vacancy) =>
        vacancy.Location ?? (vacancy.Offices.Count > 0 ? string.Join(" · ", vacancy.Offices) : null);
}
