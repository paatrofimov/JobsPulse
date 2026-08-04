using System.Text;
using JobsPulse.Core.Model;
using JobsPulse.Core.Services;
using Microsoft.Extensions.Logging;

namespace JobsPulse.Sinks.Telegram.Bot;

/// <summary>
/// Разбор команд бота. Здесь живут сценарии пользователя целиком:
///
///   /watch Finom      → поиск → список кандидатов → «1» → добавлено (сценарии 1–3)
///   /watch &lt;url&gt;      → разбор карьерной страницы (сценарий 3)
///   /list             → что отслеживается
///   /remove Finom     → снять с мониторинга
///   /help
///
/// Возвращает готовый HTML-текст ответа; отправкой занимается слушатель.
/// </summary>
public sealed class CommandRouter(
    WatchService watch,
    PendingSelectionStore pending,
    ILogger<CommandRouter> log)
{
    public async Task<string> HandleAsync(string chatId, string text, CancellationToken ct)
    {
        text = text.Trim();

        // Ответ номером на предложенный список кандидатов.
        if (int.TryParse(text, out var choice))
            return await HandleSelectionAsync(chatId, choice, ct);

        var space = text.IndexOf(' ');
        var command = (space < 0 ? text : text[..space]).ToLowerInvariant();
        var argument = space < 0 ? string.Empty : text[(space + 1)..].Trim();

        // В группах команды приходят как /watch@my_bot
        var at = command.IndexOf('@');
        if (at > 0) command = command[..at];

        return command switch
        {
            "/watch" or "/add" => await HandleWatchAsync(chatId, argument, ct),
            "/list" => RenderList(),
            "/remove" or "/unwatch" => await HandleRemoveAsync(argument, ct),
            "/start" or "/help" => Help(),
            _ => "Не понимаю. /help — список команд."
        };
    }

    private async Task<string> HandleWatchAsync(string chatId, string argument, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(argument))
            return "Укажите компанию: <code>/watch Finom</code>";

        pending.Clear(chatId);

        var result = await watch.LookupAsync(argument, ct);

        switch (result.Status)
        {
            case LookupStatus.AlreadyWatched:
                return $"Уже слежу за <b>{MessageFormatter.Escape(result.Existing!.CompanyName)}</b>.";

            case LookupStatus.NotFound:
                return $"""
                        Не нашёл «{MessageFormatter.Escape(argument)}».

                        Пришлите ссылку на их страницу вакансий — вытащу борд оттуда:
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
            return "Нечего выбирать — начните с <code>/watch НазваниеКомпании</code>.";

        if (choice < 1 || choice > candidates.Count)
        {
            pending.Set(chatId, candidates);
            return $"Введите число от 1 до {candidates.Count}.";
        }

        var candidate = candidates[choice - 1];
        var entry = await watch.AddAsync(candidate, filter: null, ct);
        log.LogInformation("Через бота добавлена компания {Company}", entry.CompanyName);

        return Added(entry, candidate);
    }

    private async Task<string> HandleRemoveAsync(string argument, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(argument))
            return "Укажите компанию: <code>/remove Finom</code>";

        var removed = await watch.RemoveAsync(argument, ct);
        return removed
            ? $"Больше не слежу за <b>{MessageFormatter.Escape(argument)}</b>."
            : $"«{MessageFormatter.Escape(argument)}» не в списке. /list — что отслеживается.";
    }

    private string RenderList()
    {
        var entries = watch.List();
        if (entries.Count == 0) return "Список пуст. Добавьте компанию: <code>/watch Finom</code>";

        var sb = new StringBuilder("<b>Отслеживаю:</b>\n");
        foreach (var e in entries)
        {
            var status = e.Enabled ? (e.SeededAt is null ? "засев" : "активно") : "выключено";
            sb.Append($"• <b>{MessageFormatter.Escape(e.CompanyName)}</b> — {status}\n");
        }

        return sb.ToString();
    }

    private static string RenderCandidates(IReadOnlyList<BoardCandidate> candidates)
    {
        var sb = new StringBuilder("Нашёл несколько вариантов — какой?\n\n");

        for (var i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            sb.Append($"<b>{i + 1}.</b> {MessageFormatter.Escape(c.DisplayName)} — {c.JobCount} вакансий\n");
        }

        sb.Append("\nОтветьте номером.");
        return sb.ToString();
    }

    private static string Added(WatchEntry entry, BoardCandidate candidate) =>
        $"""
         ✅ <b>{MessageFormatter.Escape(entry.CompanyName)}</b> добавлена ({candidate.JobCount} вакансий).

         Первый проход пройдёт молча — иначе прилетела бы вся доска сразу.
         Дальше буду присылать только изменения.
         """;

    private static string Help() =>
        """
        <b>Команды</b>
        /watch &lt;компания&gt; — начать следить (можно ссылкой на карьерную страницу)
        /list — что отслеживается
        /remove &lt;компания&gt; — снять с мониторинга
        """;
}
