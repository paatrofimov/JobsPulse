using System.Net;
using System.Text;
using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model;

namespace JobsPulse.Sinks.Telegram;

/// <summary>
/// Сборка сообщения.
///
/// Два подводных камня Telegram, из-за которых это отдельный класс:
///  • лимит 4096 символов — батч режется, а не обрезается;
///  • parse_mode=HTML принимает лишь узкий набор тегов, поэтому весь текст экранируется,
///    а описания из Greenhouse (которые ещё и HTML-энкоднуты) сюда не попадают вовсе.
/// </summary>
public static class MessageFormatter
{
    private const int TelegramLimit = 4096;
    private const int SafeLimit = 3800; // запас на экранирование

    public static IReadOnlyList<string> Format(IReadOnlyList<OutboxItem> batch)
    {
        var messages = new List<string>();
        var sb = new StringBuilder();

        foreach (var group in batch.GroupBy(i => (i.CompanyName, i.Kind)))
        {
            var block = RenderBlock(group.Key.CompanyName, group.Key.Kind, group.ToList());

            if (sb.Length + block.Length > SafeLimit && sb.Length > 0)
            {
                messages.Add(sb.ToString());
                sb.Clear();
            }

            // Один блок сам по себе длиннее лимита — режем по вакансиям.
            if (block.Length > SafeLimit)
            {
                foreach (var chunk in SplitLarge(group.Key.CompanyName, group.Key.Kind, group.ToList()))
                    messages.Add(chunk);
                continue;
            }

            sb.Append(block);
        }

        if (sb.Length > 0) messages.Add(sb.ToString());

        return messages.Select(m => m.Length > TelegramLimit ? m[..TelegramLimit] : m).ToList();
    }

    private static string RenderBlock(string company, ChangeKind kind, IReadOnlyList<OutboxItem> items)
    {
        var sb = new StringBuilder();
        sb.Append(Header(kind)).Append(" <b>").Append(Escape(company)).Append("</b>\n");

        foreach (var item in items) sb.Append(RenderVacancy(item.Vacancy));

        sb.Append('\n');
        return sb.ToString();
    }

    private static IEnumerable<string> SplitLarge(string company, ChangeKind kind, IReadOnlyList<OutboxItem> items)
    {
        const int perMessage = 8;
        for (var i = 0; i < items.Count; i += perMessage)
            yield return RenderBlock(company, kind, items.Skip(i).Take(perMessage).ToList());
    }

    private static string RenderVacancy(Vacancy v)
    {
        var location = string.IsNullOrWhiteSpace(v.Location) ? "" : $" — {Escape(v.Location)}";
        return $"• <a href=\"{Escape(v.Url)}\">{Escape(v.Title)}</a>{location}\n";
    }

    private static string Header(ChangeKind kind) => kind switch
    {
        ChangeKind.New => "\U0001F195 Новые вакансии:",
        ChangeKind.Updated => "✏️ Обновлены:",
        ChangeKind.Closed => "❌ Закрыты:",
        _ => "Изменения:"
    };

    /// <summary>Экранирование под parse_mode=HTML. Без него любой &amp; в названии роняет отправку.</summary>
    public static string Escape(string? text) =>
        WebUtility.HtmlEncode(text ?? string.Empty);
}
