using System.Net;
using System.Text;
using JobsPulse.Core.Helpers;
using JobsPulse.Core.Model.Domain;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Core.Options;
using JobsPulse.Sinks.Telegram.Infrastructure.Localization;
using Microsoft.Extensions.Options;
using Telegram.Bot.Types;

namespace JobsPulse.Sinks.Telegram.Infrastructure;

public class MessageFormatter(TimeProvider clock, IOptionsMonitor<DeliveryOptions> deliveryOptions)
{
    private const int SafeLimit = 30_000;

    public IReadOnlyList<InputRichMessage> Format(
        IReadOnlyList<OutboxItem> batch,
        BotLanguage language = BotLanguage.English)
    {
        var messages = new List<InputRichMessage>();
        var sb = new StringBuilder();

        // One block per (watchlist, company, kind, origin): the same vacancy may arrive for several watchlists at
        // once, and the reader has to see which watchlist every notification belongs to. Manually added companies
        // come first, the ones discovery brought in after them.
        foreach (var group in batch
                     .GroupBy(x => (x.WatchlistName, x.CompanyName, Kind: x.ChangeKind, x.Discovered))
                     .OrderBy(g => g.Key.WatchlistName, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(g => g.Key.Discovered)
                     .ThenByDescending(g => g.Max(PublishedAt) ?? DateTimeOffset.MinValue)
                     .ThenBy(g => g.Key.Kind))
        {
            var items = Sorted(group);
            var block = RenderBlock(
                group.Key.WatchlistName,
                group.Key.CompanyName,
                group.Key.Kind,
                group.Key.Discovered,
                items,
                language);

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
                             group.Key.Discovered,
                             items,
                             language))
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
        bool discovered,
        IReadOnlyList<OutboxItem> items,
        BotLanguage language)
    {
        var sb = new StringBuilder();

        sb.Append("<h6>")
            .Append(Header(kind, discovered, language))
            .Append(" · ")
            .Append(Escape(company));

        // A state dump has no watchlist, a real notification always has one.
        if (!string.IsNullOrWhiteSpace(watchlist))
            sb.Append(" · ").Append(Escape(watchlist));

        sb.Append("</h6>");

        foreach (var item in items)
        {
            sb.Append("<p>")
                .Append(RenderVacancy(item, language))
                .Append("</p>");
        }

        return sb.ToString();
    }

    private string RenderVacancy(OutboxItem item, BotLanguage language)
    {
        var fresh = IsFresh(item);

        var title = RenderTitleLink(item);
        var geography = RenderGeography(item, language);
        var dates = RenderDate(PublishedAt(item), language) ?? RenderDate(item.Vacancy.FirstSeenAt, language);

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
        bool discovered,
        IReadOnlyList<OutboxItem> items,
        BotLanguage language)
    {
        const int perMessage = 20;

        for (var i = 0; i < items.Count; i += perMessage)
        {
            yield return RenderBlock(
                watchlist,
                company,
                kind,
                discovered,
                items.Skip(i).Take(perMessage).ToArray(),
                language);
        }
    }

    /// <summary>
    /// Month names come from the text table, not from a culture: the solution builds with
    /// <c>InvariantGlobalization=true</c>, under which every culture formats them in English.
    /// </summary>
    private string? RenderDate(DateTimeOffset? date, BotLanguage language)
    {
        if (date is null)
            return null;

        var withYear = date.Value.Year != clock.GetUtcNow().Year;

        return BotTexts.FormatDate(date.Value, withYear, language);
    }

    private static string RenderGeography(OutboxItem item, BotLanguage language)
    {
        if (item.Vacancy.Location is not null)
            return Escape(item.Vacancy.Location);

        if (item.Vacancy.Offices.Count > 0)
            return RenderOffices(item.Vacancy.Offices);

        return BotTexts.Get(TextKey.VacancyUnknownLocation, language);
    }

    private static string RenderOffices(IReadOnlyList<string> offices) =>
        offices.Select(Escape).JoinStrings(" · ");

    /// <summary>
    /// A promoted board announces itself: its first batch is the only place the reader learns that discovery -
    /// not a manual add - brought this company in. Later batches keep the 🔎 so the two never look alike.
    /// </summary>
    private static string Header(VacancyChangeKind kind, bool discovered, BotLanguage language) =>
        (kind, discovered) switch
        {
            (VacancyChangeKind.New, true) => $"🔎 {BotTexts.Get(TextKey.NotificationNewBoard, language)}",
            (VacancyChangeKind.New, false) => $"🆕 {BotTexts.Get(TextKey.NotificationNew, language)}",
            (VacancyChangeKind.Updated, true) => $"🔎 ✏️ {BotTexts.Get(TextKey.NotificationUpdated, language)}",
            (VacancyChangeKind.Updated, false) => $"✏️ {BotTexts.Get(TextKey.NotificationUpdated, language)}",
            (VacancyChangeKind.Closed, true) => $"🔎 ❌ {BotTexts.Get(TextKey.NotificationClosed, language)}",
            (VacancyChangeKind.Closed, false) => $"❌ {BotTexts.Get(TextKey.NotificationClosed, language)}",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };

    public static string Escape(string? text) =>
        WebUtility.HtmlEncode(text ?? string.Empty);
}