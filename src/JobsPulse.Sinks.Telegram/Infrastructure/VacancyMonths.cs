using JobsPulse.Core.Model.Domain;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Sinks.Telegram.Infrastructure.Localization;

namespace JobsPulse.Sinks.Telegram.Infrastructure;

/// <summary>
/// Which month a vacancy belongs to - the third way the same feed is sliced, next to company and region.
///
/// «The month» is the board's own publication date, falling back to the update stamp and then to when we first saw
/// it, exactly the freshness every other listing sorts by. A vacancy carrying none of the three is not dropped: it
/// lands in a «date unknown» group, which sorts last because its key is zero.
/// </summary>
public static class VacancyMonths
{
    /// <summary>`yyyyMM` as a number, so «newest first» is a plain descending sort and «unknown» is 0.</summary>
    public const int Unknown = 0;

    public static DateTimeOffset? DateOf(Vacancy vacancy) =>
        vacancy.FirstPublishedAt ?? vacancy.UpdatedAt ?? vacancy.FirstSeenAt;

    public static int Of(Vacancy vacancy) =>
        DateOf(vacancy) is { } date ? date.Year * 100 + date.Month : Unknown;

    public static string Label(int month, BotLanguage language) =>
        month == Unknown
            ? BotTexts.Get(TextKey.MonthUnknown, language)
            : BotTexts.MonthName(month / 100, month % 100, language);

    public static string Glyph(int month) => month == Unknown ? "❔" : "🗓";

    /// <summary>
    /// The month of every board a feed mentions, keyed '{sourceId}/{boardId}' - the freshest one, because that is
    /// «when this company last moved». A board with nothing in the feed is simply absent.
    /// </summary>
    public static IReadOnlyDictionary<string, int> ByBoard(IReadOnlyList<Vacancy> vacancies) =>
        vacancies
            .GroupBy(v => $"{v.SourceId}/{v.BoardId}", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Max(Of), StringComparer.OrdinalIgnoreCase);
}
