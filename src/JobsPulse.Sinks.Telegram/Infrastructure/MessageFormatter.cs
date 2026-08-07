using System.Net;
using System.Text;
using JobsPulse.Core.Helpers;
using JobsPulse.Core.Model.Domain;
using JobsPulse.Core.Model.Infrastructure;
using Telegram.Bot.Types;

namespace JobsPulse.Sinks.Telegram.Infrastructure;

public static class MessageFormatter
{
    private const int SafeLimit = 30_000;

    public static IReadOnlyList<InputRichMessage> Format(
        IReadOnlyList<OutboxItem> batch)
    {
        var messages = new List<InputRichMessage>();
        var sb = new StringBuilder();

        foreach (var group in batch
                     .GroupBy(x => (x.CompanyName, Kind: x.ChangeKind))
                     .OrderBy(x => x.Key.Kind))
        {
            var items = group.ToArray();
            var block = RenderBlock(
                group.Key.CompanyName,
                group.Key.Kind,
                items);

            if (sb.Length + block.Length > SafeLimit && sb.Length > 0)
            {
                messages.Add(ToRichMessage(sb));
                sb.Clear();
            }

            if (block.Length > SafeLimit)
            {
                foreach (var chunk in SplitLarge(
                             group.Key.CompanyName,
                             group.Key.Kind,
                             items))
                {
                    messages.Add(new InputRichMessage
                    {
                        Html = chunk
                    });
                }

                continue;
            }

            sb.Append(block);
        }

        if (sb.Length > 0)
            messages.Add(ToRichMessage(sb));

        return messages;
    }

    private static InputRichMessage ToRichMessage(StringBuilder sb) =>
        new()
        {
            Html = sb.ToString()
        };

    private static string RenderBlock(
        string company,
        VacancyChangeKind kind,
        IReadOnlyList<OutboxItem> items)
    {
        var sb = new StringBuilder();

        sb.Append("<h6>")
            .Append(Header(kind))
            .Append(" · ")
            .Append(Escape(company))
            .Append("</h6>");

        foreach (var item in items)
        {
            sb.Append("<p>")
                .Append(RenderVacancy(item.Vacancy))
                .Append("</p>");
        }

        return sb.ToString();
    }

    private static string RenderVacancy(Vacancy vacancy)
    {
        var title = RenderTitleLink(vacancy);
        var geography = RenderGeography(vacancy);
        var dates = RenderDate(vacancy.UpdatedAt) ?? RenderDate(vacancy.FirstPublished);

        return $"{title}<br> {geography} · {dates}";
    }

    private static string RenderTitleLink(Vacancy vacancy) =>
        $"<a href=\"{Escape(vacancy.Url)}\"><b>{Escape(vacancy.Title)}</b></a>";

    private static IEnumerable<string> SplitLarge(
        string company,
        VacancyChangeKind kind,
        IReadOnlyList<OutboxItem> items)
    {
        const int perMessage = 20;

        for (var i = 0; i < items.Count; i += perMessage)
        {
            yield return RenderBlock(
                company,
                kind,
                items.Skip(i).Take(perMessage).ToArray());
        }
    }

    private static string? RenderDate(DateTimeOffset? date)
    {
        if (date is null)
            return null;

        return date.Value.Year == DateTime.UtcNow.Year
            ? date.Value.ToString("MMMM dd")
            : date.Value.ToString("yyyy MMMM dd");
    }

    private static string RenderGeography(Vacancy vacancy)
    {
        if (vacancy.Offices.Count > 0)
            return RenderOffices(vacancy.Offices);

        if (vacancy.Location is not null)
            return Escape(vacancy.Location);

        return "Unknown Location";
    }

    private static string RenderOffices(IReadOnlyList<string> offices) =>
        offices.Select(Escape).JoinStrings(" · ");

    private static string Header(VacancyChangeKind kind) => kind switch
    {
        VacancyChangeKind.New => "🆕",
        VacancyChangeKind.Updated => "✏️",
        VacancyChangeKind.Closed => "❌",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    public static string Escape(string? text) =>
        WebUtility.HtmlEncode(text ?? string.Empty);
}