using System.Text;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Core.Pipeline;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Sinks.Telegram.Infrastructure;

// todo (patrofimov) check if this can be replcaed by framework api
public sealed class CommandRouter(
    WatchService watch,
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
        var command = (space < 0 ? text : text[..space]).ToLowerInvariant();
        var argument = space < 0 ? string.Empty : text[(space + 1)..].Trim();

        // commands are received as /watch@my_bot
        var at = command.IndexOf('@');
        if (at > 0) command = command[..at];

        return command switch
        {
            "/watch" or "/add" => await HandleWatchAsync(chatId, argument, ct),
            "/list" => RenderList(),
            "/remove" or "/unwatch" => await HandleRemoveAsync(argument, ct),
            "/start" or "/help" => Help(),
            _ => "Unknown command. /help — commands list."
        };
    }

    private async Task<string> HandleWatchAsync(string chatId, string argument, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(argument))
            return "Specify company name: <code>/watch CompanyName</code>";

        pending.Clear(chatId);

        var result = await watch.LookupAsync(argument, ct);

        switch (result.Status)
        {
            case LookupStatus.AlreadyWatched:
                return $"Already watching for <b>{MessageFormatter.Escape(result.Existing!.CompanyName)}</b>.";

            case LookupStatus.NotFound:
                return $"""
                        Could not find «{MessageFormatter.Escape(argument)}».

                        Send url to career page — will try to parse its board directly:
                        <code>/watch https://example.com/careers</code>
                        """;

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
            return "Nothing to select — start via <code>/watch CompanyName</code>.";

        if (choice < 1 || choice > candidates.Count)
        {
            pending.Set(chatId, candidates);
            return $"Input number 1 to {candidates.Count}.";
        }

        var candidate = candidates[choice - 1];
        var entry = await watch.AddAsync(candidate, filter: null, ct);
        ctxLog.Info("Company added to watchlist via bot: {Company}", entry.CompanyName);

        return Added(entry, candidate);
    }

    private async Task<string> HandleRemoveAsync(string argument, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(argument))
            return "Specify company name: <code>/remove CompanyName</code>";

        var removed = await watch.RemoveAsync(argument, ct);
        return removed
            ? $"Stopped watching for <b>{MessageFormatter.Escape(argument)}</b>."
            : $"«{MessageFormatter.Escape(argument)}» is excluded from list. /list — to inspect watched companies.";
    }

    private string RenderList()
    {
        var entries = watch.List();
        if (entries.Count == 0) return "List is empty. Add company: <code>/watch CompanyName</code>";

        var sb = new StringBuilder("<b>Watching:</b>\n");
        foreach (var e in entries)
        {
            var status = e.Enabled ? "active" : "disabled";
            sb.Append($"\n • <b>{MessageFormatter.Escape(e.CompanyName)}</b> — {status}\n");
        }

        return sb.ToString();
    }

    private static string RenderCandidates(IReadOnlyList<BoardCandidate> candidates)
    {
        var sb = new StringBuilder("Found multiple matching candidates. Choose one:\n\n");

        for (var i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            sb.Append($"<b>{i + 1}.</b> {MessageFormatter.Escape(c.DisplayName)} — {c.JobCount} vacancies\n");
        }

        sb.Append("\nRespond with number.");
        return sb.ToString();
    }

    private static string Added(WatchEntry entry, BoardCandidate candidate) =>
        $"""
         ✅ <b>{MessageFormatter.Escape(entry.CompanyName)}</b> is added to watchlist ({candidate.JobCount} vacancies).

         First traversal will be silent — otherwise whole board will be sent immediately.
         From now on only board changes will be tracked.
         """;

    private static string Help() =>
        """
        <b>Commands</b>
        /watch &lt;CompanyName&gt; — start watching (can process career page URL instead of CompanyName)
        /list — the list of the companies being watched
        /remove &lt;CompanyName&gt; — stop watching
        """;
}