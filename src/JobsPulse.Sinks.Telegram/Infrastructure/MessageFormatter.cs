using System.Net;
using System.Text;
using JobsPulse.Core.Helpers;
using JobsPulse.Core.Model.Domain;
using JobsPulse.Core.Model.Infrastructure;

namespace JobsPulse.Sinks.Telegram.Infrastructure;

public static class MessageFormatter
{
    private const int TelegramLimit = 4096;
    private const int SafeLimit = 3800;

    public static IReadOnlyList<string> Format(IReadOnlyList<OutboxItem> batch)
    {
        var messages = new List<string>();
        var sb = new StringBuilder();

        foreach (var group in batch.GroupBy(i => (i.CompanyName, Kind: i.ChangeKind)).OrderBy(x => x.Key.Kind))
        {
            var block = RenderBlock(group.Key.CompanyName, group.Key.Kind, [.. group]);

            if (sb.Length + block.Length > SafeLimit && sb.Length > 0)
            {
                messages.Add(sb.ToString());
                sb.Clear();
            }

            if (block.Length > SafeLimit)
            {
                foreach (var chunk in SplitLarge(group.Key.CompanyName, group.Key.Kind, [.. group]))
                    messages.Add(chunk);
                continue;
            }

            sb.Append(block);
        }

        if (sb.Length > 0) messages.Add(sb.ToString());

        return [.. messages.Select(m => m.Length > TelegramLimit ? m[..TelegramLimit] : m)];
    }

    private static string RenderBlock(string company, VacancyChangeKind kind, IReadOnlyList<OutboxItem> items)
    {
        var sb = new StringBuilder();
        sb.Append(Header(kind)).Append(" <b>").Append(Escape(company)).Append("</b>\n");

        foreach (var item in items)
            sb.AppendLine(RenderVacancy(item.Vacancy)).AppendLine();

        sb.AppendLine();
        return sb.ToString();
    }

    private static IEnumerable<string> SplitLarge(string company, VacancyChangeKind kind, IReadOnlyList<OutboxItem> items)
    {
        const int perMessage = 8;
        for (var i = 0; i < items.Count; i += perMessage)
            yield return RenderBlock(company, kind, [.. items.Skip(i).Take(perMessage)]);
    }

    private static string RenderVacancy(Vacancy v)
    {
        var title = RenderTitleLink(v);
        var geography = RenderGeography(v);
        var dates = RenderPublishedUpdated(v);
        return $"{title}\n\t{geography} | {dates}";
    }

    private static string RenderTitleLink(Vacancy v)
    {
        return $"• <a href=\"{Escape(v.Url)}\">{Escape(v.Title)}</a>";
    }

    private static string? RenderPublishedUpdated(Vacancy vacancy)
    {
        var published = RenderDate(vacancy.FirstPublished);
        var updated = RenderDate(vacancy.UpdatedAt);

        if (published != null && updated != null)
        {
            if (published == updated)
                return published;

            return $"{updated} (published {published})";
        }

        if (published != null)
            return published;

        return updated;
    }

    private static string? RenderDate(DateTimeOffset? date)
    {
        var currentYear = DateTime.UtcNow.Year;
        if (currentYear != date?.Year)
            return date?.ToString("yyyy MMMM dd");
        return date.Value.ToString("M");
    }

    private static string? RenderGeography(Vacancy vacancy)
    {
        if (vacancy.Offices.Count > 0)
            return RenderOffices(vacancy.Offices);
        if (vacancy.Location != null)
            return RenderLocation(vacancy.Location);
        return "Unknown Location";
    }

    private static string? RenderOffices(IReadOnlyList<string> offices)
    {
        if (offices.Count > 0)
            return offices.Select(Escape).JoinStrings(" · ");
        return null;
    }

    private static string? RenderLocation(string location) => Escape(location);

    private static string Header(VacancyChangeKind kind) => kind switch
    {
        VacancyChangeKind.New => "\U0001F195 New vacancies:",
        VacancyChangeKind.Updated => "✏️ Updated:",
        VacancyChangeKind.Closed => "❌ Closed:",
        _ => "Changes:"
    };

    public static string Escape(string? text) =>
        WebUtility.HtmlEncode(text ?? string.Empty);
}