using System.Text;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Sinks.Telegram.Infrastructure.Localization;

namespace JobsPulse.Sinks.Telegram.Infrastructure;

/// <summary>
/// Renders <see cref="WatchlistStats"/> - the same body for the periodic digest and for the statistics screen, only
/// the header differs. Static and IO-free, so it is testable on its own. A company in a top list is a link when
/// <c>links</c> knows it (<see cref="DeepLinks.Companies"/>) - a tap opens its vacancies.
/// </summary>
public static class StatsFormatter
{
    public static string Format(
        WatchlistStats stats,
        BotLanguage language,
        bool digest,
        Func<string, string?>? links = null,
        bool sincePrevious = false)
    {
        var title = !digest ? TextKey.StatsTitle
            : sincePrevious ? TextKey.StatsDigestSinceTitle
            : TextKey.StatsDigestTitle;
        var name = MessageFormatter.Escape(stats.WatchlistName);

        return $"<h6>{BotTexts.Get(title, language, name, Days(stats.Days, language))}</h6>"
               + $"<p>{BotTexts.Get(TextKey.ChangesPeriod, language, Period(stats.From, stats.To, language))}</p>"
               + Body(stats, language, links, withOpen: true);
    }

    /// <summary>«+3» / «−2» / «±0».</summary>
    public static string Delta(int delta) =>
        delta switch
        {
            > 0 => $"+{delta}",
            < 0 => $"−{-delta}",
            _ => "±0"
        };

    /// <summary>What one polling or registry run changed in a watchlist, under what the run walked.</summary>
    public static string FormatRun(
        TraversalRunReport report,
        BotLanguage language,
        Func<string, string?>? links = null)
    {
        var title = report.Kind == TraversalKind.Registry ? TextKey.RunTitleRegistry : TextKey.RunTitlePolling;
        var stats = report.Stats;

        var walked = report.Cycle is { } cycle
            ? BotTexts.Get(TextKey.RunWalked, language, cycle.BoardsProcessed, cycle.Failed, cycle.Changes)
            : BotTexts.Get(TextKey.RunUnfinished, language);

        // The run itself and the period its changes piled up in: a board polled days ago reports days of changes.
        var changes = report.Cycle is { ChangesSince: { } since }
            ? BotTexts.Get(TextKey.ChangesPeriod, language, Period(since, stats.To, language))
            : report.Cycle is { BoardsProcessed: > 0 }
                ? BotTexts.Get(TextKey.ChangesFirstPoll, language)
                : null;

        return $"<h6>{BotTexts.Get(title, language, MessageFormatter.Escape(stats.WatchlistName))}</h6>"
               + $"<p>{BotTexts.Get(TextKey.RunPeriod, language, Period(stats.From, stats.To, language))}"
               + (changes is null ? "" : $"<br>{changes}")
               + "</p>"
               + $"<p>{walked}</p>"
               + Body(stats, language, links, withOpen: false);
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

        var collections = run.Report is { FirstCollection: { } first, LastCollection: { } last }
            ? "<br>" + BotTexts.Get(
                TextKey.DiscoveryCollections,
                language,
                first == last ? first : $"{first} … {last}")
            : "";

        return $"<h6>{BotTexts.Get(title, language)}</h6>"
               + $"<p>{BotTexts.Get(TextKey.RunPeriod, language, Period(run.StartedAt, run.FinishedAt, language))}"
               + collections
               + "</p>"
               + $"<p>{counts}</p>";
    }

    /// <summary>«September 29, 07:25 – October 02, 07:25 UTC», the year added only across a new year.</summary>
    public static string Period(DateTimeOffset from, DateTimeOffset to, BotLanguage language)
    {
        var withYear = from.Year != to.Year;

        return BotTexts.Get(TextKey.StatsPeriod, language, Moment(from, withYear, language), Moment(to, withYear, language));
    }

    /// <summary>
    /// The vacancies, then the companies: what is open against the start of the period (<paramref name="withOpen"/>;
    /// a run counts only its own events, so its numbers do not add up to that), what opened and what ended - so that
    /// «were + opened − closed − dropped = open now» - and how much of what opened already ended.
    /// </summary>
    private static string Body(WatchlistStats stats, BotLanguage language, Func<string, string?>? links, bool withOpen)
    {
        var sb = new StringBuilder("<p>");
        var before = withOpen ? stats.OpenAtStart : null;
        var after = withOpen ? stats.OpenAtEnd : null;
        var open = before is not null && after is not null;

        if (open)
        {
            sb.Append(BotTexts.Get(
                TextKey.StatsOpenVacancies, language, after!.Vacancies, before!.Vacancies, Delta(after.Vacancies - before.Vacancies)));
            sb.Append("<br>");
        }

        sb.Append(BotTexts.Get(TextKey.StatsOpened, language, stats.Opened));
        if (stats.OpenedAndEnded > 0)
            sb.Append(BotTexts.Get(TextKey.StatsOpenedThenEnded, language, stats.OpenedAndEnded));

        sb.Append("<br>").Append(BotTexts.Get(TextKey.StatsClosed, language, stats.Closed));
        if (stats.Dropped > 0)
            sb.Append("<br>").Append(BotTexts.Get(TextKey.StatsDropped, language, stats.Dropped));

        sb.Append("</p><p>");

        if (open)
        {
            sb.Append(BotTexts.Get(
                TextKey.StatsOpenCompanies, language, after!.Companies, before!.Companies, Delta(after.Companies - before.Companies)));
            sb.Append("<br>");
        }

        // A company both new and emptied got its first vacancy and lost its last one inside the period.
        var newThenEmptied = stats.NewCompanies.Intersect(stats.EmptiedCompanies, StringComparer.OrdinalIgnoreCase).Count();

        sb.Append(BotTexts.Get(TextKey.StatsNewCompanies, language, stats.NewCompanies.Count));
        if (newThenEmptied > 0)
            sb.Append(BotTexts.Get(TextKey.StatsNewThenEmptied, language, newThenEmptied));

        sb.Append("<br>").Append(BotTexts.Get(TextKey.StatsEmptiedCompanies, language, stats.EmptiedCompanies.Count));
        sb.Append("</p>");

        sb.Append($"<p><b>{BotTexts.Get(TextKey.StatsTopActivity, language)}</b><br>");
        sb.Append(Top(
            stats.TopByActivity,
            x => x.CompanyName,
            x => BotTexts.Get(
                TextKey.StatsActivityRow,
                language,
                x.Activity.Events,
                ActivityRanks.Breakdown(x.Activity, language)),
            links,
            language));
        sb.Append("</p>");

        sb.Append($"<p><b>{BotTexts.Get(TextKey.StatsTopOpened, language)}</b><br>");
        sb.Append(Top(
            stats.TopByOpened,
            x => x.CompanyName,
            x => BotTexts.Get(TextKey.StatsOpenedRow, language, x.Count),
            links,
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
        Func<string, string?>? links,
        BotLanguage language)
    {
        if (rows.Count == 0)
            return BotTexts.Get(TextKey.StatsNothing, language);

        return string.Join(
            "<br>",
            rows.Select((row, i) => $"{i + 1}. {Company(name(row), links)} — {value(row)}"));
    }

    private static string Company(string name, Func<string, string?>? links) =>
        links?.Invoke(name) is { } url
            ? $"<a href=\"{MessageFormatter.Escape(url)}\">{MessageFormatter.Escape(name)}</a>"
            : MessageFormatter.Escape(name);
}
