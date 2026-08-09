using System.Text;
using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model.Domain;
using JobsPulse.Core.Model.Domain.Extensions;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Core.Pipeline;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Sinks.Telegram.Infrastructure;

public sealed class CommandRouter(
    WatchService watch,
    PollingOrchestrator orchestrator,
    IStateStore stateStore,
    IVacancySink sink,
    PendingSelectionStore pending,
    ILog log)
{
    private readonly ILog ctxLog = log.ForContext<CommandRouter>();

    public async Task<string> HandleAsync(string chatId, string text, CancellationToken ct)
    {
        text = text.Trim();

        // Responded with candidate number
        if (int.TryParse(text, out var choice))
            return await HandleSelectionAsync(chatId, choice, ct);

        var space = text.IndexOf(' ');
        var command = (space < 0 ? text : text[..space]).ToLowerInvariant().TrimStart('/');
        var argument = space < 0 ? string.Empty : text[(space + 1)..].Trim();

        // commands are received as /watch@my_bot
        var at = command.IndexOf('@');
        if (at > 0)
            command = command[..at];

        return command switch
        {
            BotCommandCatalog.Watch or "add" => await HandleWatchAsync(chatId, argument, ct),
            BotCommandCatalog.List => RenderList(),
            BotCommandCatalog.Remove or "unwatch" => await HandleRemoveAsync(argument, ct),
            BotCommandCatalog.ForceCycle => await HandleForceCycleAsync(ct),
            BotCommandCatalog.ShowState => await HandleShowStateAsync(ct),
            BotCommandCatalog.DropData => await HandleDropDataAsync(ct),
            BotCommandCatalog.Help or "start" => BotCommandCatalog.RenderHelp(),
            _ => "<p>Unknown command. /help — commands list.</p>"
        };
    }

    private async Task<string> HandleWatchAsync(string chatId, string argument, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(argument))
            return "<p>Specify company name: <code>/watch CompanyName</code></p>";

        pending.Clear(chatId);

        var result = await watch.LookupAsync(argument, ct);

        switch (result.Status)
        {
            case LookupStatus.AlreadyWatched:
                return $"<p>Already watching for <b>{MessageFormatter.Escape(result.Existing!.CompanyName)}</b>.</p>";

            case LookupStatus.NotFound:
                return $"<p>Could not find «{MessageFormatter.Escape(argument)}».</p>"
                       + "<p>Send url to career page — will try to parse its board directly:<br>"
                       + "<code>/watch https://example.com/careers</code></p>";

            case LookupStatus.Found when result.Candidates.Count == 1:
                var only = result.Candidates[0];
                var entry = await watch.AddAsync(only, filter: null, ct);
                return Added(entry, only);

            default:
                pending.Set(chatId, result.Candidates);
                return RenderCandidates(result.Candidates);
        }
    }

    private async Task<string> HandleSelectionAsync(string chatId, int choice, CancellationToken ct)
    {
        var candidates = pending.Take(chatId);
        if (candidates is null)
            return "<p>Nothing to select — start via <code>/watch CompanyName</code>.</p>";

        if (choice < 1 || choice > candidates.Count)
        {
            pending.Set(chatId, candidates);
            return $"<p>Input number 1 to {candidates.Count}.</p>";
        }

        var candidate = candidates[choice - 1];
        var entry = await watch.AddAsync(candidate, filter: null, ct);
        ctxLog.Info("Company added to watchlist via bot: {Company}", entry.CompanyName);

        return Added(entry, candidate);
    }

    private async Task<string> HandleRemoveAsync(string argument, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(argument))
            return "<p>Specify company name: <code>/remove CompanyName</code></p>";

        var removed = await watch.RemoveAsync(argument, ct);
        return removed
            ? $"<p>Stopped watching for <b>{MessageFormatter.Escape(argument)}</b>.</p>"
            : $"<p>«{MessageFormatter.Escape(argument)}» is excluded from list.<br>/list — to inspect watched companies.</p>";
    }

    private async Task<string> HandleForceCycleAsync(CancellationToken ct)
    {
        var result = await orchestrator.TryRunCycleAsync(ct);

        if (!result.Started)
            return "<p>Cycle is already running — a new one is not started.</p>";

        var report = result.Report;
        return "<h6>✅ Cycle finished</h6>"
               + $"<p>boards: <b>{report.BoardsProcessed}</b><br>"
               + $"fetched: <b>{report.VacanciesFetched}</b><br>"
               + $"matched: <b>{report.VacanciesMatched}</b><br>"
               + $"changes: <b>{report.Changes}</b><br>"
               + $"errors: <b>{report.Failed}</b></p>";
    }

    private async Task<string> HandleDropDataAsync(CancellationToken ct)
    {
        var purged = await stateStore.PurgeAllAsync(ct);
        ctxLog.Warn("State dropped via bot command");

        return "<h6>🧹 Data dropped</h6>"
               + $"<p>seen vacancies: <b>{purged.SeenVacanciesDeleted}</b><br>"
               + $"outbox: <b>{purged.OutboxDeleted}</b></p>"
               + "<p>Next cycle will re-seed the boards.</p>";
    }

    private async Task<string> HandleShowStateAsync(CancellationToken ct)
    {
        var rows = await stateStore.LoadAllAsync(ct);
        if (rows.Count == 0)
            return "<p>Vacancies table is empty.</p>";

        // Stored rows are rendered by the very same pipeline as real notifications.
        var result = await sink.DeliverAsync(ToOutboxItems(rows), ct);

        if (!result.Success)
            return $"<p>❌ Failed to render state: {MessageFormatter.Escape(result.Error)}</p>";

        return $"<p>Stored vacancies: <b>{rows.Count}</b>, open: <b>{rows.Count(r => r.IsOpen)}</b>.</p>";
    }

    private IReadOnlyList<OutboxItem> ToOutboxItems(IReadOnlyList<SeenVacancySnapshot> rows)
    {
        var companies = watch.List()
            .GroupBy(e => $"{e.VacancySourceId}/{e.BoardId}", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().CompanyName, StringComparer.OrdinalIgnoreCase);

        return rows
            .Select(row =>
            {
                var vacancy = row.Vacancy;
                var kind = row.IsOpen ? VacancyChangeKind.New : VacancyChangeKind.Closed;
                var board = $"{vacancy.SourceId}/{vacancy.BoardId}";

                return new OutboxItem
                {
                    DedupKey = vacancy.ToDedupKey(kind, vacancy.ContentHash),
                    ChangeKind = kind,
                    CompanyName = companies.GetValueOrDefault(board, board),
                    Vacancy = vacancy
                };
            })
            .ToList();
    }

    private string RenderList()
    {
        var entries = watch.List();
        if (entries.Count == 0)
            return "<p>List is empty. Add company: <code>/watch CompanyName</code></p>";

        var sb = new StringBuilder("<h6>Watching</h6><p>");
        foreach (var e in entries)
        {
            var status = e.Enabled ? "active" : "disabled";
            sb.Append($"• <b>{MessageFormatter.Escape(e.CompanyName)}</b> — {status}<br>");
        }

        return sb.Append("</p>").ToString();
    }

    private static string RenderCandidates(IReadOnlyList<BoardCandidate> candidates)
    {
        var sb = new StringBuilder("<h6>Found multiple matching candidates</h6><p>");

        for (var i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            sb.Append($"<b>{i + 1}.</b> {MessageFormatter.Escape(c.DisplayName)} — {c.JobCount} vacancies<br>");
        }

        sb.Append("</p><p>Respond with number.</p>");
        return sb.ToString();
    }

    private static string Added(WatchEntry entry, BoardCandidate candidate) =>
        $"<h6>✅ {MessageFormatter.Escape(entry.CompanyName)}</h6>"
        + $"<p>Added to watchlist ({candidate.JobCount} vacancies).</p>"
        + "From now on only board changes will be tracked.</p>";
}
