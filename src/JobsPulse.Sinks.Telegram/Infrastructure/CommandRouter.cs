using System.Text;
using System.Text.Json;
using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Helpers;
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
    IBoardRegistryStorage boardRegistry,
    IBoardDiscoveryService discovery,
    IVacancySink sink,
    PendingSelectionStore pending,
    ILog log)
{
    private const int BoardsPageSize = 30;
    private const int BoardsScanLimit = 1000;

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
            BotCommandCatalog.Watchlists or "list" => await RenderWatchlistsAsync(ct),
            BotCommandCatalog.Watchlist => await RenderWatchlistAsync(argument, ct),
            BotCommandCatalog.WatchlistAdd => await HandleWatchlistAddAsync(argument, ct),
            BotCommandCatalog.WatchlistRemove => await HandleWatchlistRemoveAsync(argument, ct),
            BotCommandCatalog.WatchlistEnable => await HandleWatchlistEnabledAsync(argument, true, ct),
            BotCommandCatalog.WatchlistDisable => await HandleWatchlistEnabledAsync(argument, false, ct),
            BotCommandCatalog.Filter => await HandleFilterAsync(argument, ct),
            BotCommandCatalog.BoardAdd => await HandleBoardAddAsync(argument, ct),
            BotCommandCatalog.BoardRemove or "unwatch" => await HandleBoardRemoveAsync(argument, ct),
            BotCommandCatalog.Watch or "add" => await HandleWatchAsync(chatId, argument, ct),
            BotCommandCatalog.ForceCycle => await HandleForceCycleAsync(ct),
            BotCommandCatalog.ShowState => await HandleShowStateAsync(ct),
            BotCommandCatalog.DropData => await HandleDropDataAsync(ct),
            BotCommandCatalog.Boards => await HandleBoardsAsync(argument, ct),
            BotCommandCatalog.RegistryRemove => await HandleRegistryRemoveAsync(argument, ct),
            BotCommandCatalog.Discover => await HandleDiscoverAsync(),
            BotCommandCatalog.Help or "start" => BotCommandCatalog.RenderHelp(),
            _ => "<p>Unknown command. /help — commands list.</p>"
        };
    }

    private async Task<string> RenderWatchlistsAsync(CancellationToken ct)
    {
        var watchlists = await watch.ListAsync(ct);
        if (watchlists.Count == 0)
            return "<p>No watchlists yet. Create one: <code>/watchlist_add .NET Europe</code></p>";

        var matches = await stateStore.CountMatchesByWatchlistAsync(ct);

        var sb = new StringBuilder("<h6>Watchlists</h6><p>");

        foreach (var watchlist in watchlists)
        {
            var status = watchlist.Enabled ? "active" : "paused";
            var enabledEntries = watchlist.Entries.Count(e => e.Enabled);

            sb.Append($"• <b>{MessageFormatter.Escape(watchlist.Name)}</b> <code>#{watchlist.Id}</code> — {status}, "
                      + $"boards: <b>{enabledEntries}</b> of {watchlist.Entries.Count}, "
                      + $"matching: <b>{matches.GetValueOrDefault(watchlist.Id)}</b><br>");
        }

        return sb.Append("</p><p>/watchlist &lt;name&gt; — details.</p>").ToString();
    }

    private async Task<string> RenderWatchlistAsync(string argument, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(argument))
            return "<p>Specify a watchlist: <code>/watchlist .NET Europe</code></p>";

        var watchlist = await watch.ResolveAsync(argument, ct);
        if (watchlist is null)
            return NotFound(argument);

        var sb = new StringBuilder($"<h6>{MessageFormatter.Escape(watchlist.Name)} <code>#{watchlist.Id}</code></h6>");

        sb.Append($"<p>state: <b>{(watchlist.Enabled ? "active" : "paused")}</b><br>");
        sb.Append($"interval: <b>{watchlist.IntervalMinutesOverride?.ToString() ?? "default"}</b><br>");
        sb.Append($"filter: <code>{MessageFormatter.Escape(watchlist.Filter.ToString())}</code></p>");

        if (watchlist.Entries.Count == 0)
        {
            return sb.Append("<p>No boards yet: <code>/board_add "
                             + $"{MessageFormatter.Escape(watchlist.Name)} greenhouse nebius</code></p>").ToString();
        }

        sb.Append("<p>");
        foreach (var entry in watchlist.Entries)
        {
            var status = entry.Enabled ? string.Empty : " — disabled";

            sb.Append($"• <b>{MessageFormatter.Escape(entry.CompanyName)}</b> <code>#{entry.Id}</code> "
                      + $"<code>{MessageFormatter.Escape(entry.BoardKey)}</code>{status}<br>");
        }

        return sb.Append("</p><p>/board_remove &lt;watchlist&gt; &lt;entryId&gt; — to drop a board.</p>").ToString();
    }

    private async Task<string> HandleWatchlistAddAsync(string argument, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(argument))
            return "<p>Specify a name: <code>/watchlist_add .NET Europe</code></p>";

        var created = await watch.CreateAsync(Unquote(argument), ct);

        return created is null
            ? $"<p>Watchlist «{MessageFormatter.Escape(argument)}» already exists.</p>"
            : $"<h6>✅ {MessageFormatter.Escape(created.Name)}</h6>"
              + $"<p>Watchlist created with id <code>#{created.Id}</code>.<br>"
              + "Add boards: <code>/board_add "
              + $"{MessageFormatter.Escape(created.Name)} greenhouse nebius</code><br>"
              + $"Set the filter: <code>/filter {created.Id} {{\"titleAnyOf\":[\"backend\"]}}</code></p>";
    }

    private async Task<string> HandleWatchlistRemoveAsync(string argument, CancellationToken ct)
    {
        var watchlist = await watch.ResolveAsync(argument, ct);
        if (watchlist is null)
            return NotFound(argument);

        var removed = await watch.RemoveAsync(watchlist, ct);

        return removed
            ? $"<p>Watchlist <b>{MessageFormatter.Escape(watchlist.Name)}</b> is removed with its boards. "
              + "Stored vacancies are kept — they are global state.</p>"
            : "<p>Nothing was removed.</p>";
    }

    private async Task<string> HandleWatchlistEnabledAsync(string argument, bool enabled, CancellationToken ct)
    {
        var watchlist = await watch.ResolveAsync(argument, ct);
        if (watchlist is null)
            return NotFound(argument);

        await watch.SetEnabledAsync(watchlist, enabled, ct);

        return $"<p>Watchlist <b>{MessageFormatter.Escape(watchlist.Name)}</b> is "
               + (enabled ? "active again." : "paused — its boards are not polled.")
               + "</p>";
    }

    private async Task<string> HandleFilterAsync(string argument, CancellationToken ct)
    {
        var (reference, tail) = SplitReference(argument);

        var watchlist = await watch.ResolveAsync(reference, ct);
        if (watchlist is null)
            return NotFound(reference);

        if (string.IsNullOrWhiteSpace(tail))
        {
            return $"<h6>Filter of {MessageFormatter.Escape(watchlist.Name)}</h6>"
                   + $"<p><code>{MessageFormatter.Escape(watchlist.Filter.ToString())}</code></p>"
                   + "<p>Replace it with json:<br>"
                   + $"<code>/filter {watchlist.Id} {{\"titleAnyOf\":[\"backend\",\"sre\"],\"postedWithinDays\":60}}</code><br>"
                   + $"Clear it: <code>/filter {watchlist.Id} {{}}</code></p>";
        }

        FilterSpec? filter;
        try
        {
            filter = JsonSerializer.Deserialize<FilterSpec>(tail, JsonSerializerOptionsFactory.Instance);
        }
        catch (JsonException ex)
        {
            return $"<p>❌ Filter json is invalid: {MessageFormatter.Escape(ex.Message)}</p>";
        }

        if (filter is null)
            return "<p>❌ Filter json is empty.</p>";

        await watch.SetFilterAsync(watchlist, filter, ct);

        return $"<h6>✅ {MessageFormatter.Escape(watchlist.Name)}</h6>"
               + $"<p>Filter is now <code>{MessageFormatter.Escape(filter.ToString())}</code>.<br>"
               + "Stored vacancies are re-evaluated on the next cycle.</p>";
    }

    private async Task<string> HandleBoardAddAsync(string argument, CancellationToken ct)
    {
        var (reference, tail) = SplitReference(argument);

        var watchlist = await watch.ResolveAsync(reference, ct);
        if (watchlist is null)
            return NotFound(reference);

        var parts = tail.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
            return "<p>Specify source and board: <code>/board_add "
                   + $"{MessageFormatter.Escape(watchlist.Name)} greenhouse nebius [Nebius]</code></p>";

        var result = await watch.AddBoardAsync(watchlist, parts[0], parts[1], parts.ElementAtOrDefault(2), ct);

        return result.Status switch
        {
            BoardAddStatus.Added =>
                $"<h6>✅ {MessageFormatter.Escape(result.Entry!.CompanyName)}</h6>"
                + $"<p>Added to <b>{MessageFormatter.Escape(watchlist.Name)}</b> as entry <code>#{result.Entry.Id}</code>.</p>",
            BoardAddStatus.UnknownSource =>
                $"<p>Unknown source «{MessageFormatter.Escape(result.Subject)}».</p>",
            BoardAddStatus.BoardNotFound =>
                $"<p>Board «{MessageFormatter.Escape(result.Subject)}» does not answer. "
                + "Pass the company name explicitly to add it anyway.</p>",
            _ => NotFound(reference)
        };
    }

    private async Task<string> HandleBoardRemoveAsync(string argument, CancellationToken ct)
    {
        var (reference, tail) = SplitReference(argument);

        var watchlist = await watch.ResolveAsync(reference, ct);
        if (watchlist is null)
            return NotFound(reference);

        if (string.IsNullOrWhiteSpace(tail))
            return $"<p>Specify the entry id: <code>/board_remove {watchlist.Id} 12</code><br>"
                   + "/watchlist &lt;name&gt; — to see the ids.</p>";

        var removed = await watch.RemoveEntryAsync(watchlist, tail, ct);

        return removed
            ? $"<p>Removed from <b>{MessageFormatter.Escape(watchlist.Name)}</b>.</p>"
            : $"<p>«{MessageFormatter.Escape(tail)}» is not in <b>{MessageFormatter.Escape(watchlist.Name)}</b>.</p>";
    }

    private async Task<string> HandleWatchAsync(string chatId, string argument, CancellationToken ct)
    {
        var (reference, query) = SplitReference(argument);

        var watchlist = await watch.ResolveAsync(reference, ct);
        if (watchlist is null)
            return NotFound(reference);

        if (string.IsNullOrWhiteSpace(query))
            return $"<p>Specify a company: <code>/watch {watchlist.Id} CompanyName</code></p>";

        pending.Clear(chatId);

        var result = await watch.LookupAsync(watchlist, query, ct);

        switch (result.Status)
        {
            case LookupStatus.AlreadyWatched:
                return $"<p><b>{MessageFormatter.Escape(result.Existing!.CompanyName)}</b> is already in "
                       + $"<b>{MessageFormatter.Escape(watchlist.Name)}</b>.</p>";

            case LookupStatus.NotFound:
                return $"<p>Could not find «{MessageFormatter.Escape(query)}».</p>"
                       + "<p>Send url to career page — will try to parse its board directly:<br>"
                       + $"<code>/watch {watchlist.Id} https://example.com/careers</code></p>";

            case LookupStatus.Found when result.Candidates.Count == 1:
                var only = result.Candidates[0];
                var entry = await watch.AddCandidateAsync(watchlist, only, ct);
                return entry is null ? NotFound(reference) : Added(watchlist, entry, only);

            default:
                pending.Set(chatId, watchlist.Id, result.Candidates);
                return RenderCandidates(watchlist, result.Candidates);
        }
    }

    private async Task<string> HandleSelectionAsync(string chatId, int choice, CancellationToken ct)
    {
        var selection = pending.Take(chatId);
        if (selection is null)
            return "<p>Nothing to select — start via <code>/watch &lt;watchlist&gt; CompanyName</code>.</p>";

        if (choice < 1 || choice > selection.Candidates.Count)
        {
            pending.Set(chatId, selection.WatchlistId, selection.Candidates);
            return $"<p>Input number 1 to {selection.Candidates.Count}.</p>";
        }

        var watchlist = await watch.ResolveAsync(selection.WatchlistId.ToString(), ct);
        if (watchlist is null)
            return "<p>The watchlist is gone — nothing was added.</p>";

        var candidate = selection.Candidates[choice - 1];
        var entry = await watch.AddCandidateAsync(watchlist, candidate, ct);
        if (entry is null)
            return "<p>The watchlist is gone — nothing was added.</p>";

        ctxLog.Info("Board added via bot: {Company} → {Watchlist}", entry.CompanyName, watchlist.Name);

        return Added(watchlist, entry, candidate);
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
               + $"watchlist matches: <b>{purged.WatchlistMatchesDeleted}</b><br>"
               + $"outbox: <b>{purged.OutboxDeleted}</b><br>"
               + $"boards: <b>{purged.BoardsDeleted}</b><br>"
               + $"crawl index states: <b>{purged.CrawlIndexStateDeleted}</b></p>"
               + "<p>Watchlists themselves are configuration and are kept.</p>";
    }

    private async Task<string> HandleBoardsAsync(string argument, CancellationToken ct)
    {
        var counts = await boardRegistry.CountBySourceAsync(ct);
        if (counts.Count == 0)
            return "<p>Board registry is empty. /discover — fill it from crawl indexes.</p>";

        var sourceId = string.IsNullOrWhiteSpace(argument) ? null : argument;

        // Boards are ranked by how many relevant vacancies are stored for them; the board size only breaks ties.
        var stored = await stateStore.CountOpenByBoardAsync(ct);

        var boards = (await boardRegistry.ListAsync(sourceId, BoardsScanLimit, ct))
            .Select(b => (Board: b, Stored: stored.GetValueOrDefault($"{b.SourceId}/{b.BoardId}")))
            .OrderByDescending(x => x.Stored)
            .ThenByDescending(x => x.Board.JobCount)
            .Take(BoardsPageSize)
            .ToList();

        var sb = new StringBuilder("<h6>Board registry</h6><p>");
        foreach (var (source, count) in counts.OrderBy(c => c.Key, StringComparer.Ordinal))
            sb.Append($"{MessageFormatter.Escape(source)}: <b>{count}</b><br>");

        sb.Append($"</p><p>Top {boards.Count} by relevant vacancies:</p><p>");

        foreach (var (board, storedCount) in boards)
        {
            var title = MessageFormatter.Escape(board.DisplayName ?? board.BoardId);
            var link = board.BoardUrl is null
                ? title
                : $"<a href=\"{MessageFormatter.Escape(board.BoardUrl)}\">{title}</a>";

            sb.Append($"• {link} — <code>{MessageFormatter.Escape(board.SourceId)} {MessageFormatter.Escape(board.BoardId)}</code>"
                      + $" — <b>{storedCount}</b> relevant of {board.JobCount}<br>");
        }

        return sb.Append("</p>").ToString();
    }

    private async Task<string> HandleRegistryRemoveAsync(string argument, CancellationToken ct)
    {
        var parts = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
            return "<p>Specify source and board: <code>/registry_remove greenhouse acme</code></p>";

        var removed = await boardRegistry.RemoveAsync(parts[0], parts[1], ct);
        return removed
            ? $"<p>Board <b>{MessageFormatter.Escape(parts[1])}</b> is removed from the registry.</p>"
            : "<p>No such board in the registry.</p>";
    }

    private Task<string> HandleDiscoverAsync()
    {
        // A full re-walk of the crawl indexes takes hours — it must not block the command loop.
        _ = Task.Run(() => RunDiscoveryAsync(), CancellationToken.None);

        return Task.FromResult(
            "<h6>🔎 Discovery started</h6>"
            + "<p>Crawl indexes are re-walked from scratch, already known boards are skipped.<br>"
            + "The result is written to the log; a second run is ignored while this one is alive.<br>"
            + "/boards — to inspect the registry.</p>");
    }

    private async Task RunDiscoveryAsync()
    {
        try
        {
            var report = await discovery.RunAsync(full: true, CancellationToken.None);

            if (!report.Started)
            {
                ctxLog.Info("Forced discovery is skipped — another run is in progress");
                return;
            }

            ctxLog.Info(
                "Forced discovery finished: {Collections} indexes, {Tokens} tokens, {Added} new boards, "
                + "{Pending} collections left pending ({Failed} failed)",
                report.CollectionsProcessed, report.TokensFound, report.BoardsAdded,
                report.CollectionsPending, report.CollectionsFailed);
        }
        catch (Exception ex)
        {
            ctxLog.Error(ex, "Forced discovery has failed");
        }
    }

    private async Task<string> HandleShowStateAsync(CancellationToken ct)
    {
        var rows = await stateStore.LoadAllAsync(ct);
        if (rows.Count == 0)
            return "<p>Vacancies table is empty.</p>";

        // Stored rows are rendered by the very same pipeline as real notifications.
        var result = await sink.DeliverAsync(await ToOutboxItemsAsync(rows, ct), ct);

        if (!result.Success)
            return $"<p>❌ Failed to render state: {MessageFormatter.Escape(result.Error)}</p>";

        return $"<p>Stored vacancies: <b>{rows.Count}</b>, open: <b>{rows.Count(r => r.IsOpen)}</b>.</p>";
    }

    private async Task<IReadOnlyList<OutboxItem>> ToOutboxItemsAsync(
        IReadOnlyList<SeenVacancySnapshot> rows,
        CancellationToken ct)
    {
        var companies = (await watch.ListAsync(ct))
            .SelectMany(w => w.Entries)
            .GroupBy(e => e.BoardKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().CompanyName, StringComparer.OrdinalIgnoreCase);

        return rows
            .Select(row =>
            {
                var vacancy = row.Vacancy;
                var kind = row.IsOpen ? VacancyChangeKind.New : VacancyChangeKind.Closed;
                var board = $"{vacancy.SourceId}/{vacancy.BoardId}";

                // A dump belongs to no watchlist - it is state, not a notification.
                return new OutboxItem
                {
                    DedupKey = vacancy.ToDedupKey(kind, vacancy.ContentHash, watchlistId: null),
                    ChangeKind = kind,
                    CompanyName = companies.GetValueOrDefault(board, board),
                    Vacancy = vacancy
                };
            })
            .ToList();
    }

    private static string RenderCandidates(Watchlist watchlist, IReadOnlyList<BoardCandidate> candidates)
    {
        var sb = new StringBuilder(
            $"<h6>Found multiple matching candidates for {MessageFormatter.Escape(watchlist.Name)}</h6><p>");

        for (var i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            sb.Append($"<b>{i + 1}.</b> {MessageFormatter.Escape(c.DisplayName)} — {c.JobCount} vacancies<br>");
        }

        sb.Append("</p><p>Respond with number.</p>");
        return sb.ToString();
    }

    private static string Added(Watchlist watchlist, WatchlistEntry entry, BoardCandidate candidate) =>
        $"<h6>✅ {MessageFormatter.Escape(entry.CompanyName)}</h6>"
        + $"<p>Added to <b>{MessageFormatter.Escape(watchlist.Name)}</b> as entry <code>#{entry.Id}</code> "
        + $"({candidate.JobCount} vacancies).<br>"
        + "From now on only board changes will be tracked.</p>";

    private static string NotFound(string reference) =>
        $"<p>No watchlist «{MessageFormatter.Escape(reference)}». /watchlists — to see them all.</p>";

    /// <summary>
    /// The first argument is the watchlist: a quoted name, a single word or a numeric id.
    /// Quotes are what makes «Platform / SRE» addressable by name at all.
    /// </summary>
    private static (string Reference, string Tail) SplitReference(string argument)
    {
        argument = argument.Trim();
        if (argument.Length == 0)
            return (string.Empty, string.Empty);

        if (argument[0] is '"' or '«')
        {
            var closing = argument.IndexOfAny(['"', '»'], 1);
            if (closing > 0)
                return (argument[1..closing].Trim(), argument[(closing + 1)..].Trim());
        }

        var space = argument.IndexOf(' ');
        return space < 0
            ? (argument, string.Empty)
            : (argument[..space].Trim(), argument[(space + 1)..].Trim());
    }

    private static string Unquote(string value)
    {
        value = value.Trim();

        return value.Length > 1 && value[0] is '"' or '«' && value[^1] is '"' or '»'
            ? value[1..^1].Trim()
            : value;
    }
}
