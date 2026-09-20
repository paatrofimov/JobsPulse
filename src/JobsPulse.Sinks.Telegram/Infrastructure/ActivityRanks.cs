using JobsPulse.Core.Model.Domain;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Sinks.Telegram.Infrastructure.Localization;
using JobsPulse.Sinks.Telegram.Models;

namespace JobsPulse.Sinks.Telegram.Infrastructure;

/// <summary>
/// The company activity indicator on screen: how many vacancy events a board produces per month
/// (<see cref="BoardActivity"/>), which band that puts it in and how the two are written.
///
/// The bands are deliberately coarse. The number itself is noisy - an ATS that rewrites every posting on a nightly
/// reindex inflates it - so the ordering is by the raw rate but the reader is given a band, and a rate that looks
/// impossible is the fastest way to notice a source bug rather than a hiring spree.
/// </summary>
public static class ActivityRanks
{
    public static string Key(WatchlistEntry entry) => $"{entry.VacancySourceId}/{entry.BoardId}";

    public static string Key(Vacancy vacancy) => $"{vacancy.SourceId}/{vacancy.BoardId}";

    public static BoardActivity Of(IReadOnlyDictionary<string, BoardActivity> activity, string boardKey) =>
        activity.GetValueOrDefault(boardKey, BoardActivity.None);

    public static ActivityRank Rank(BoardActivity activity) =>
        activity.PerMonth switch
        {
            >= 20 => ActivityRank.Blazing,
            >= 5 => ActivityRank.Hot,
            > 0 => ActivityRank.Warm,
            _ => ActivityRank.Still
        };

    public static string Glyph(ActivityRank rank) =>
        rank switch
        {
            ActivityRank.Blazing => "🔥",
            ActivityRank.Hot => "🌡",
            ActivityRank.Warm => "🍃",
            _ => "💤"
        };

    public static string Name(ActivityRank rank, BotLanguage language) =>
        BotTexts.Get(
            rank switch
            {
                ActivityRank.Blazing => TextKey.ActivityBlazing,
                ActivityRank.Hot => TextKey.ActivityHot,
                ActivityRank.Warm => TextKey.ActivityWarm,
                _ => TextKey.ActivityStill
            },
            language);

    public static string Label(ActivityRank rank, BotLanguage language) =>
        $"{Glyph(rank)} {Name(rank, language)}";

    /// <summary>«4.2/mo» - one decimal, because the difference between 4 and 5 a month is the whole signal.</summary>
    public static string Rate(BoardActivity activity, BotLanguage language) =>
        BotTexts.Get(TextKey.ActivityRate, language, activity.PerMonth.ToString("0.#"));

    /// <summary>The three numbers behind the rate, for the row that has room for them.</summary>
    public static string Breakdown(BoardActivity activity, BotLanguage language) =>
        BotTexts.Get(
            TextKey.ActivityBreakdown,
            language,
            activity.Opened,
            activity.Changed,
            activity.Closed);
}
