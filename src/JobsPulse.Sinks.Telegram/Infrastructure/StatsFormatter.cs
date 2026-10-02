using System.Text;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Sinks.Telegram.Infrastructure.Localization;

namespace JobsPulse.Sinks.Telegram.Infrastructure;

/// <summary>
/// Renders <see cref="WatchlistStats"/> - the same body for the periodic digest and for the statistics screen, only
/// the header differs. Static and IO-free, so it is testable on its own.
/// </summary>
public static class StatsFormatter
{
    public static string Format(WatchlistStats stats, BotLanguage language, bool digest)
    {
        var title = digest ? TextKey.StatsDigestTitle : TextKey.StatsTitle;
        var name = MessageFormatter.Escape(stats.WatchlistName);

        return $"<h6>{BotTexts.Get(title, language, name, Days(stats.Days, language))}</h6>"
               + Period(stats.From, stats.To, language)
               + Body(stats, language);
    }

    /// <summary>What one polling or registry run changed in a watchlist, under what the run walked.</summary>
    public static string FormatRun(TraversalRunReport report, BotLanguage language)
    {
        var title = report.Kind == TraversalKind.Registry ? TextKey.RunTitleRegistry : TextKey.RunTitlePolling;
        var stats = report.Stats;

        var walked = report.Cycle is { } cycle
            ? BotTexts.Get(TextKey.RunWalked, language, cycle.BoardsProcessed, cycle.Failed, cycle.Changes)
            : BotTexts.Get(TextKey.RunUnfinished, language);

        return $"<h6>{BotTexts.Get(title, language, MessageFormatter.Escape(stats.WatchlistName))}</h6>"
               + Period(stats.From, stats.To, language)
               + $"<p>{walked}</p>"
               + Body(stats, language);
    }

    /// <summary>What one discovery run mined. Not bound to a watchlist.</summary>
    public static string FormatDiscovery(DiscoveryRunReport run, BotLanguage language)
    {
        var title = run.Full ? TextKey.DiscoveryTitleFull : TextKey.DiscoveryTitle;

        var counts = run.Report is { } r
            ? BotTexts.Get(
                TextKey.DiscoveryCounts,
                language,
                r.CollectionsProcessed,
                r.CollectionsFailed,
                r.CollectionsPending,
                r.RecordsSeen,
                r.TokensFound,
                r.Validated,
                r.BoardsAdded)
            : BotTexts.Get(TextKey.RunUnfinished, language);

        return $"<h6>{BotTexts.Get(title, language)}</h6>"
               + Period(run.StartedAt, run.FinishedAt, language)
               + $"<p>{counts}</p>";
    }

    private static string Period(DateTimeOffset from, DateTimeOffset to, BotLanguage language)
    {
        var withYear = from.Year != to.Year;

        return $"<p>{BotTexts.Get(TextKey.StatsPeriod, language, Moment(from, withYear, language), Moment(to, withYear, language))}</p>";
    }

    private static string Body(WatchlistStats stats, BotLanguage language)
    {
        var sb = new StringBuilder();

        sb.Append($"<p>{BotTexts.Get(TextKey.StatsOpened, language, stats.Opened)}<br>");
        sb.Append($"{BotTexts.Get(TextKey.StatsClosed, language, stats.Closed)}<br>");
        sb.Append($"{BotTexts.Get(TextKey.StatsNewCompanies, language, stats.NewCompanies.Count)}<br>");
        sb.Append($"{BotTexts.Get(TextKey.StatsEmptiedCompanies, language, stats.EmptiedCompanies.Count)}</p>");

        sb.Append($"<p><b>{BotTexts.Get(TextKey.StatsTopActivity, language)}</b><br>");
        sb.Append(Top(
            stats.TopByActivity,
            x => x.CompanyName,
            x => BotTexts.Get(
                TextKey.StatsActivityRow,
                language,
                x.Activity.Events,
                ActivityRanks.Breakdown(x.Activity, language)),
            language));
        sb.Append("</p>");

        sb.Append($"<p><b>{BotTexts.Get(TextKey.StatsTopOpened, language)}</b><br>");
        sb.Append(Top(
            stats.TopByOpened,
            x => x.CompanyName,
            x => BotTexts.Get(TextKey.StatsOpenedRow, language, x.Count),
            language));
        sb.Append("</p>");

        return sb.ToString();
    }

    /// <summary>«3 days» / «3 дня» - Russian needs three plural forms, English two.</summary>
    public static string Days(int days, BotLanguage language)
    {
        var key = language == BotLanguage.Russian
            ? RussianPlural(days)
            : days == 1 ? TextKey.StatsDaysOne : TextKey.StatsDaysMany;

        return BotTexts.Get(key, language, days);
    }

    private static TextKey RussianPlural(int n)
    {
        var mod10 = n % 10;
        var mod100 = n % 100;

        if (mod10 == 1 && mod100 != 11)
            return TextKey.StatsDaysOne;

        if (mod10 is >= 2 and <= 4 && mod100 is < 12 or > 14)
            return TextKey.StatsDaysFew;

        return TextKey.StatsDaysMany;
    }

    private static string Moment(DateTimeOffset moment, bool withYear, BotLanguage language) =>
        $"{BotTexts.FormatDate(moment, withYear, language)}, {BotTexts.FormatTime(moment)}";

    private static string Top<T>(
        IReadOnlyList<T> rows,
        Func<T, string> name,
        Func<T, string> value,
        BotLanguage language)
    {
        if (rows.Count == 0)
            return BotTexts.Get(TextKey.StatsNothing, language);

        return string.Join(
            "<br>",
            rows.Select((row, i) => $"{i + 1}. {MessageFormatter.Escape(name(row))} — {value(row)}"));
    }
}
