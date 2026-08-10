using System.Net;
using System.Text;
using JobsPulse.Core.Helpers;
using JobsPulse.Core.Model.Domain;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Core.Options;
using Microsoft.Extensions.Options;
using Telegram.Bot.Types;

namespace JobsPulse.Sinks.Telegram.Infrastructure;

public class MessageFormatter(TimeProvider clock, IOptionsMonitor<DeliveryOptions> deliveryOptions)
{
    private const int SafeLimit = 30_000;

    public IReadOnlyList<InputRichMessage> Format(
        IReadOnlyList<OutboxItem> batch)
    {
        var messages = new List<InputRichMessage>();
        var sb = new StringBuilder();

        // One block per (watchlist, company, kind): the same vacancy may arrive for several watchlists at once,
        // and the reader has to see which watchlist every notification belongs to.
        foreach (var group in batch
                     .GroupBy(x => (x.WatchlistName, x.CompanyName, Kind: x.ChangeKind))
                     .OrderBy(g => g.Key.WatchlistName, StringComparer.OrdinalIgnoreCase)
                     .ThenByDescending(g => g.Max(PublishedAt) ?? DateTimeOffset.MinValue)
                     .ThenBy(g => g.Key.Kind))
        {
            var items = Sorted(group);
            var block = RenderBlock(
                group.Key.WatchlistName,
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
                             group.Key.WatchlistName,
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

    private string RenderBlock(
        string? watchlist,
        string company,
        VacancyChangeKind kind,
        IReadOnlyList<OutboxItem> items)
    {
        var sb = new StringBuilder();

        sb.Append("<h6>")
            .Append(Header(kind))
            .Append(" · ")
            .Append(Escape(company));

        // A state dump has no watchlist, a real notification always has one.
        if (!string.IsNullOrWhiteSpace(watchlist))
            sb.Append(" · ").Append(Escape(watchlist));

        sb.Append("</h6>");

        foreach (var item in items)
        {
            sb.Append("<p>")
                .Append(RenderVacancy(item))
                .Append("</p>");
        }

        return sb.ToString();
    }

    private string RenderVacancy(OutboxItem item)
    {
        var fresh = IsFresh(item);

        var title = RenderTitleLink(item);
        var geography = RenderGeography(item);
        var dates = RenderDate(PublishedAt(item)) ?? RenderDate(item.Vacancy.FirstSeenAt);

        var line = $"{title}<br> {geography} · {dates}";

        return fresh ? $"🔥 <b>{line}</b>" : line;
    }

    /// <summary>Freshness is the board's own publication date; the update date is the fallback.</summary>
    private static DateTimeOffset? PublishedAt(OutboxItem item) =>
        item.Vacancy.FirstPublishedAt ?? item.Vacancy.UpdatedAt;

    private bool IsFresh(OutboxItem item)
    {
        var days = deliveryOptions.CurrentValue.FreshVacancyDays;
        if (days <= 0)
            return false;

        return PublishedAt(item) is { } published && published >= clock.GetUtcNow().AddDays(-days);
    }

    private static IReadOnlyList<OutboxItem> Sorted(IEnumerable<OutboxItem> items) =>
    [
        .. items.OrderByDescending(PublishedAt).ThenBy(i => i.Vacancy.Title, StringComparer.OrdinalIgnoreCase)
    ];

    private static string RenderTitleLink(OutboxItem item) =>
        $"<a href=\"{Escape(item.Vacancy.Url)}\"><b>{Escape(item.Vacancy.Title)}</b></a>";

    private IEnumerable<string> SplitLarge(
        string? watchlist,
        string company,
        VacancyChangeKind kind,
        IReadOnlyList<OutboxItem> items)
    {
        const int perMessage = 20;

        for (var i = 0; i < items.Count; i += perMessage)
        {
            yield return RenderBlock(
                watchlist,
                company,
                kind,
                items.Skip(i).Take(perMessage).ToArray());
        }
    }

    private string? RenderDate(DateTimeOffset? date)
    {
        if (date is null)
            return null;

        return date.Value.Year == clock.GetUtcNow().Year
            ? date.Value.ToString("MMMM dd")
            : date.Value.ToString("yyyy MMMM dd");
    }

    private static string RenderGeography(OutboxItem item)
    {
        if (item.Vacancy.Location is not null)
            return Escape(item.Vacancy.Location);

        if (item.Vacancy.Offices.Count > 0)
            return RenderOffices(item.Vacancy.Offices);

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