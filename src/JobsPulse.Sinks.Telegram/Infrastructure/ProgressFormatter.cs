using System.Globalization;
using System.Text;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Sinks.Telegram.Models;

namespace JobsPulse.Sinks.Telegram.Infrastructure;

/// <summary>
/// The traversal progress as one html block, in plain words: for every routine when it last ran, how it ended and
/// what it did, and how much of its board set has been walked - «polling last ran at 09:18, walked 2084 boards of
/// 2084». Pure and static: the admin screen and the <c>/progress</c> command render the very same text.
///
/// Russian or English by the language of the reader - the one part of the operator surface that is read rather than
/// typed.
/// </summary>
public static class ProgressFormatter
{
    /// <summary>The longest a job may run (discovery, 330 minutes) with a margin - a run older than this is dead.</summary>
    private static readonly TimeSpan MaxRunDuration = TimeSpan.FromHours(7);

    public static string Render(ProgressSnapshot snapshot, BotLanguage language)
    {
        var t = new Texts(language);
        var sb = new StringBuilder($"<h6>🛰 {t.Pick("Прогресс обхода", "Traversal progress")}</h6>");

        sb.Append($"<p><i>{t.Pick("Время — UTC.", "Times are UTC.")}</i></p>");

        sb.Append(RenderPolling(snapshot, t));
        sb.Append(RenderRegistry(snapshot, t));
        sb.Append(RenderDiscovery(snapshot, t));
        sb.Append(RenderCleanup(snapshot, t));

        return sb.ToString();
    }

    private static string RenderPolling(ProgressSnapshot snapshot, Texts t)
    {
        var sb = new StringBuilder(
            $"<p><b>📋 {t.Pick("Доски watchlist'ов", "Watchlist boards")}</b> (polling)<br>");

        var last = RenderRuns(sb, snapshot, JobRunRoles.Polling, t);

        if (last?.Summary.BoardsProcessed is { } processed)
        {
            sb.Append(t.Pick(
                $"В этом запуске обошли <b>{t.Num(processed)}</b> {t.Boards(processed)}",
                $"This run walked <b>{t.Num(processed)}</b> boards"));

            AppendErrors(sb, last.Summary, t);

            sb.Append(t.Pick(
                $"; получено вакансий {t.Num(last.Summary.VacanciesFetched ?? 0)}, изменений {t.Num(last.Summary.Changes ?? 0)}<br>",
                $"; {t.Num(last.Summary.VacanciesFetched ?? 0)} vacancies fetched, {t.Num(last.Summary.Changes ?? 0)} changes<br>"));
        }

        var coverage = snapshot.Watchlist;
        if (coverage.Total.Total == 0)
            return sb.Append(t.Pick("В включённых watchlist'ах нет досок.", "No boards in the enabled watchlists.") + "</p>")
                .ToString();

        sb.Append(t.Pick(
            $"Досок в watchlist'ах: <b>{t.Num(coverage.Total.Total)}</b>. ",
            $"Boards in the watchlists: <b>{t.Num(coverage.Total.Total)}</b>. "));

        sb.Append(t.Pick(
            $"С начала последнего завершённого запуска опрошено <b>{t.Num(coverage.Total.Fresh)}</b> ({coverage.Total.FreshPercent}%)",
            $"Polled since the last finished run started: <b>{t.Num(coverage.Total.Fresh)}</b> ({coverage.Total.FreshPercent}%)"));

        AppendLag(sb, coverage.Total, snapshot.Now, t);

        return sb.Append("</p>").ToString();
    }

    private static string RenderRegistry(ProgressSnapshot snapshot, Texts t)
    {
        var sb = new StringBuilder(
            $"<p><b>🗂 {t.Pick("Реестр досок", "Board registry")}</b> (registry)<br>");

        var last = RenderRuns(sb, snapshot, JobRunRoles.Registry, t);

        if (last?.Summary.BoardsProcessed is { } processed)
        {
            sb.Append(t.Pick(
                $"В этом запуске обошли <b>{t.Num(processed)}</b> {t.Boards(processed)}",
                $"This run walked <b>{t.Num(processed)}</b> boards"));

            AppendErrors(sb, last.Summary, t);

            sb.Append(t.Pick(
                $"; получено вакансий {t.Num(last.Summary.VacanciesFetched ?? 0)}, подходящих под фильтры сохранено {t.Num(last.Summary.VacanciesStored ?? 0)}<br>",
                $"; {t.Num(last.Summary.VacanciesFetched ?? 0)} vacancies fetched, {t.Num(last.Summary.VacanciesStored ?? 0)} matching ones stored<br>"));
        }

        var coverage = snapshot.Registry;

        sb.Append(t.Pick(
            $"Активных досок вне watchlist'ов: <b>{t.Num(coverage.Total.Total)}</b>",
            $"Active boards outside the watchlists: <b>{t.Num(coverage.Total.Total)}</b>"));

        if (snapshot.RegistryInactive > 0)
            sb.Append(t.Pick(
                $" (ещё {t.Num(snapshot.RegistryInactive)} отключены — перестали отвечать)",
                $" ({t.Num(snapshot.RegistryInactive)} more switched off - they stopped answering)"));

        sb.Append(".<br>");

        if (coverage.Total.Total == 0)
            return sb.Append("</p>").ToString();

        sb.Append(t.Pick(
            $"За последние 24 ч опрошено <b>{t.Num(coverage.Total.Fresh)}</b> ({coverage.Total.FreshPercent}%)",
            $"Polled within the last 24 h: <b>{t.Num(coverage.Total.Fresh)}</b> ({coverage.Total.FreshPercent}%)"));

        AppendLag(sb, coverage.Total, snapshot.Now, t);
        sb.Append("<br>");

        foreach (var (sourceId, stats) in coverage.BySource)
        {
            sb.Append(t.Pick(
                $"• <code>{MessageFormatter.Escape(sourceId)}</code>: {t.Num(stats.Fresh)} из {t.Num(stats.Total)} ({stats.FreshPercent}%)",
                $"• <code>{MessageFormatter.Escape(sourceId)}</code>: {t.Num(stats.Fresh)} of {t.Num(stats.Total)} ({stats.FreshPercent}%)"));

            if (stats.Never > 0)
                sb.Append(t.Pick($", ни разу не опрошено {t.Num(stats.Never)}", $", never polled {t.Num(stats.Never)}"));

            sb.Append("<br>");
        }

        return sb.Append("</p>").ToString();
    }

    private static string RenderDiscovery(ProgressSnapshot snapshot, Texts t)
    {
        var sb = new StringBuilder(
            $"<p><b>🔎 {t.Pick("Поиск новых досок", "Board discovery")}</b> (discovery)<br>");

        var last = RenderRuns(sb, snapshot, JobRunRoles.Discovery, t);

        if (last?.Summary is { CollectionsProcessed: { } processed } summary)
        {
            sb.Append(t.Pick(
                $"В этом запуске обработано коллекций Common Crawl: <b>{processed}</b>",
                $"This run processed <b>{processed}</b> Common Crawl collections"));

            if (summary.CollectionsFailed is > 0)
                sb.Append(t.Pick($", с ошибкой {summary.CollectionsFailed}", $", {summary.CollectionsFailed} failed"));

            if (summary.CollectionsPending is > 0)
                sb.Append(t.Pick(
                    $", отложено на следующий запуск {summary.CollectionsPending}",
                    $", {summary.CollectionsPending} left for the next run"));

            sb.Append(t.Pick(
                $"; ссылок {t.Num(summary.RecordsSeen ?? 0)}, кандидатов {t.Num(summary.TokensFound ?? 0)}, новых досок <b>{t.Num(summary.BoardsAdded ?? 0)}</b><br>",
                $"; {t.Num(summary.RecordsSeen ?? 0)} urls, {t.Num(summary.TokensFound ?? 0)} candidates, <b>{t.Num(summary.BoardsAdded ?? 0)}</b> new boards<br>"));
        }

        if (snapshot.Discovery.Current is { } iteration)
        {
            sb.Append(t.Pick(
                $"Итерация #{iteration.Iteration}: пройдено {iteration.CollectionsDone} из {iteration.CollectionsTotal} коллекций окна",
                $"Iteration #{iteration.Iteration}: {iteration.CollectionsDone} of {iteration.CollectionsTotal} collections of the window walked"));

            if (iteration.CollectionsFailed > 0)
                sb.Append(t.Pick(
                    $", не прочитано {iteration.CollectionsFailed}",
                    $", {iteration.CollectionsFailed} unreadable"));

            sb.Append(t.Pick(
                $", найдено новых досок {t.Num(iteration.BoardsAdded)}<br>",
                $", {t.Num(iteration.BoardsAdded)} new boards found<br>"));
        }

        var known = snapshot.KnownBySource.Values.Sum();

        if (known == 0)
            return sb.Append(t.Pick("Досок ещё не найдено.", "No boards found yet.") + "</p>").ToString();

        var bySource = string.Join(", ", snapshot.KnownBySource
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => $"{MessageFormatter.Escape(p.Key)} {t.Num(p.Value)}"));

        sb.Append(t.Pick(
            $"Всего известно досок: <b>{t.Num(known)}</b> — {bySource}",
            $"Boards known in total: <b>{t.Num(known)}</b> - {bySource}"));

        return sb.Append("</p>").ToString();
    }

    private static string RenderCleanup(ProgressSnapshot snapshot, Texts t)
    {
        var sb = new StringBuilder(
            $"<p><b>🧹 {t.Pick("Очистка отправленных уведомлений", "Delivered notifications cleanup")}</b> (cleanup)<br>");

        RenderRuns(sb, snapshot, JobRunRoles.Cleanup, t);

        return sb.Append("</p>").ToString();
    }

    /// <summary>
    /// The last run of <paramref name="role"/> with its outcome, and the last successful one when that is not the
    /// same. Returns the run whose numbers are worth showing - the last one that has finished.
    /// </summary>
    private static JobRun? RenderRuns(StringBuilder sb, ProgressSnapshot snapshot, string role, Texts t)
    {
        var runs = snapshot.Runs.Where(r => r.Role == role).ToList();

        if (runs.Count == 0)
        {
            sb.Append(t.Pick("Запусков за последние 30 дней не было.<br>", "No runs within the last 30 days.<br>"));
            return null;
        }

        var last = runs[0];
        sb.Append(t.Pick("Последний запуск: ", "Last run: "));
        sb.Append(DescribeRun(last, snapshot.Now, t));
        sb.Append("<br>");

        // A run still walking has no numbers yet - the ones shown are of the run before it.
        var finished = runs.FirstOrDefault(r => r.Outcome != JobRunOutcome.Running);

        if (last.Outcome is not JobRunOutcome.Succeeded
            && runs.FirstOrDefault(r => r.Outcome == JobRunOutcome.Succeeded) is { } succeeded)
        {
            sb.Append(t.Pick("Последний успешный: ", "Last successful: "));
            sb.Append($"{Stamp(succeeded.StartedAt)} ({t.Ago(snapshot.Now - succeeded.StartedAt)})<br>");
        }

        if (finished is not null && !ReferenceEquals(finished, last))
            sb.Append(t.Pick(
                $"Цифры ниже — запуска {Stamp(finished.StartedAt)}:<br>",
                $"The numbers below are of the run of {Stamp(finished.StartedAt)}:<br>"));

        return finished;
    }

    private static string DescribeRun(JobRun run, DateTimeOffset now, Texts t)
    {
        var started = $"{Stamp(run.StartedAt)} ({t.Ago(now - run.StartedAt)})";

        if (run.Outcome == JobRunOutcome.Running)
        {
            return now - run.StartedAt > MaxRunDuration
                ? t.Pick(
                    $"{started} — ⚠️ так и не завершился, процесс, видимо, был убит",
                    $"{started} - ⚠️ never finished, the process was probably killed")
                : t.Pick(
                    $"{started} — ⏳ идёт сейчас, {t.Span(now - run.StartedAt)}",
                    $"{started} - ⏳ running now, for {t.Span(now - run.StartedAt)}");
        }

        var took = run.FinishedAt is { } finished
            ? t.Pick($", длился {t.Span(finished - run.StartedAt)}", $", took {t.Span(finished - run.StartedAt)}")
            : string.Empty;

        return run.Outcome switch
        {
            JobRunOutcome.Succeeded => t.Pick($"{started}{took} — ✅ успешно", $"{started}{took} - ✅ succeeded"),
            JobRunOutcome.TimedOut => t.Pick(
                $"{started}{took} — ⏱ остановлен по лимиту времени, следующий запуск продолжит",
                $"{started}{took} - ⏱ stopped at its time limit, the next run continues"),
            JobRunOutcome.Stopped => t.Pick($"{started}{took} — ⏹ остановлен извне", $"{started}{took} - ⏹ stopped from outside"),
            _ => t.Pick($"{started}{took} — ❌ упал", $"{started}{took} - ❌ failed")
                 + (run.Error is { } error ? $": <code>{MessageFormatter.Escape(error)}</code>" : string.Empty)
        };
    }

    private static void AppendErrors(StringBuilder sb, JobRunSummary summary, Texts t)
    {
        if (summary.BoardsFailed is > 0)
            sb.Append(t.Pick(
                $", с ошибкой {t.Num(summary.BoardsFailed.Value)}",
                $", {t.Num(summary.BoardsFailed.Value)} with an error"));
    }

    /// <summary>How far behind the walk is: boards never reached, and the oldest poll of the set.</summary>
    private static void AppendLag(StringBuilder sb, CoverageStats stats, DateTimeOffset now, Texts t)
    {
        if (stats.Never > 0)
            sb.Append(t.Pick(
                $"; ни разу не опрашивались: <b>{t.Num(stats.Never)}</b>",
                $"; never polled: <b>{t.Num(stats.Never)}</b>"));

        if (stats.Oldest is { } oldest)
            sb.Append(t.Pick(
                $"; самый давний опрос — {t.Ago(now - oldest)}",
                $"; the oldest poll was {t.Ago(now - oldest)}"));

        sb.Append('.');
    }

    private static string Stamp(DateTimeOffset moment) =>
        moment.ToUniversalTime().ToString("dd.MM HH:mm", CultureInfo.InvariantCulture);

    /// <summary>The wording of one language: numbers, durations and the plural of «board».</summary>
    private sealed class Texts(BotLanguage language)
    {
        private readonly bool russian = language == BotLanguage.Russian;

        // The host runs in globalization-invariant mode, so there is no ru-RU culture to borrow the grouping from.
        private readonly NumberFormatInfo numbers = language == BotLanguage.Russian
            ? new NumberFormatInfo { NumberGroupSeparator = "\u00A0" }
            : NumberFormatInfo.InvariantInfo;

        public string Pick(string ru, string en) => russian ? ru : en;

        public string Num(long value) => value.ToString("N0", numbers);

        public string Boards(int count)
        {
            if (!russian)
                return count == 1 ? "board" : "boards";

            var tens = count % 100;
            var ones = count % 10;

            if (tens is >= 11 and <= 14)
                return "досок";

            return ones switch
            {
                1 => "доску",
                2 or 3 or 4 => "доски",
                _ => "досок"
            };
        }

        public string Ago(TimeSpan span) => Pick($"{Span(span)} назад", $"{Span(span)} ago");

        /// <summary>«2 ч 5 мин» / «2h 5m», «3 д 4 ч» for anything longer than a day.</summary>
        public string Span(TimeSpan span)
        {
            if (span < TimeSpan.Zero)
                span = TimeSpan.Zero;

            if (span.TotalDays >= 1)
                return Pick($"{(int)span.TotalDays} д {span.Hours} ч", $"{(int)span.TotalDays}d {span.Hours}h");

            if (span.TotalHours >= 1)
                return Pick($"{(int)span.TotalHours} ч {span.Minutes} мин", $"{(int)span.TotalHours}h {span.Minutes}m");

            return span.TotalMinutes >= 1
                ? Pick($"{(int)span.TotalMinutes} мин", $"{(int)span.TotalMinutes}m")
                : Pick("меньше минуты", "less than a minute");
        }
    }
}
