using System.Net;
using System.Text;
using JobsPulse.Core.Helpers;
using JobsPulse.Core.Infrastructure;
using JobsPulse.Core.Model.Domain;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Core.Options;
using JobsPulse.Sinks.Telegram.Infrastructure.Localization;
using Microsoft.Extensions.Options;
using Telegram.Bot.Types;

namespace JobsPulse.Sinks.Telegram.Infrastructure;

/// <summary>
/// Turns an outbox batch into messages. The unit is a **time window**, not a change: everything one cycle found
/// within <c>Delivery:GroupChangesWithinMinutes</c> is reported together, under one «what happened between 18:35 and
/// 18:40» header, because that is how it actually happened - a cycle that walks forty boards used to produce forty
/// separate messages of two lines each.
///
/// Inside a window the batch is split per watchlist (the same vacancy legitimately arrives for several) and then
/// folded into one collapsed <c>&lt;details&gt;</c> block per company, exactly like the browsable lists: the message
/// opens as the list of company headers and unfolding one shows its vacancies. A block mixes the three change kinds,
/// so the summary counts them and every line carries its own glyph.
///
/// Blocks are ordered manual companies before discovered ones, then by their freshest vacancy; vacancies inside a
/// block by freshness too.
/// </summary>
public class MessageFormatter(TimeProvider clock, IOptionsMonitor<DeliveryOptions> deliveryOptions)
{
    private const int SafeLimit = 30_000;

    private const string DetailsClose = "</details>";

    private static readonly VacancyChangeKind[] KindOrder =
        [VacancyChangeKind.New, VacancyChangeKind.Updated, VacancyChangeKind.Closed];

    public IReadOnlyList<InputRichMessage> Format(
        IReadOnlyList<OutboxItem> batch,
        BotLanguage language = BotLanguage.English)
    {
        var messages = new List<InputRichMessage>();
        var sb = new StringBuilder();

        foreach (var section in Sections(batch))
        {
            var header = RenderSectionHeader(section, language);
            var pending = true;

            foreach (var block in section.Companies.SelectMany(c => RenderCompanyBlocks(c, language)))
            {
                var needed = (pending ? header.Length : 0) + block.Length;

                if (sb.Length + needed > SafeLimit && sb.Length > 0)
                {
                    messages.Add(ToRichMessage(sb));
                    sb.Clear();
                    pending = true;
                }

                if (pending)
                {
                    sb.Append(header);
                    pending = false;
                }

                sb.Append(block);
            }
        }

        if (sb.Length > 0)
            messages.Add(ToRichMessage(sb));

        return messages;
    }

    /// <summary>
    /// One section per (time window, watchlist), oldest window first - the reader is catching up, so the story is
    /// told forwards. A synthetic item (the <c>/show_state</c> dump) has no watchlist and simply gets a section
    /// without a name.
    /// </summary>
    private IEnumerable<Section> Sections(IReadOnlyList<OutboxItem> batch)
    {
        var window = DeliveryWindow.Of(deliveryOptions.CurrentValue.GroupChangesWithinMinutes);
        var now = clock.GetUtcNow();

        // A synthetic item (the `/show_state` dump) is never stored and carries no stamp - it happens now.
        return batch
            .GroupBy(item => (
                Window: DeliveryWindow.Floor(item.CreatedAt == default ? now : item.CreatedAt, window),
                item.WatchlistName))
            .OrderBy(g => g.Key.Window)
            .ThenBy(g => g.Key.WatchlistName, StringComparer.OrdinalIgnoreCase)
            .Select(g => new Section(
                g.Key.Window,
                g.Key.Window + window,
                g.Key.WatchlistName,
                [.. Companies(g)]));
    }

    /// <summary>
    /// The blocks of one section. A company that produced both a new and a changed vacancy is still one block - the
    /// reader asks «what happened at this company», not «what happened of kind Updated».
    /// </summary>
    private static IEnumerable<CompanyBatch> Companies(IEnumerable<OutboxItem> items) =>
        items
            .GroupBy(x => (x.CompanyName, x.Discovered))
            .Select(g => new CompanyBatch(g.Key.CompanyName, g.Key.Discovered, Sorted(g)))
            .OrderBy(c => c.Discovered)
            .ThenByDescending(c => c.Items.Max(PublishedAt) ?? DateTimeOffset.MinValue)
            .ThenBy(c => c.Company, StringComparer.OrdinalIgnoreCase);

    private static InputRichMessage ToRichMessage(StringBuilder sb) =>
        new()
        {
            Html = sb.ToString()
        };

    private string RenderSectionHeader(Section section, BotLanguage language)
    {
        var window = BotTexts.Get(
            TextKey.NotificationWindow,
            language,
            BotTexts.FormatDate(section.From, section.From.Year != clock.GetUtcNow().Year, language),
            BotTexts.FormatTime(section.From),
            BotTexts.FormatTime(section.To));

        var sb = new StringBuilder("<h6>").Append(window);

        // A state dump has no watchlist, a real notification always has one.
        if (!string.IsNullOrWhiteSpace(section.Watchlist))
            sb.Append(" · ").Append(Escape(section.Watchlist));

        sb.Append("</h6>");

        var changes = section.Companies.Sum(c => c.Items.Count);

        return sb.Append("<p>")
            .Append(BotTexts.Get(TextKey.NotificationWindowCounts, language, changes, section.Companies.Count))
            .Append("</p>")
            .ToString();
    }

    /// <summary>
    /// One company, folded. The summary has to carry the whole answer on its own - it is the only line visible while
    /// the block is collapsed - so it names the company and counts the changes per kind.
    ///
    /// A promoted board announces itself: a discovered company reporting new vacancies is the one place the reader
    /// learns that discovery, not a manual add, brought it in.
    /// </summary>
    private IEnumerable<string> RenderCompanyBlocks(CompanyBatch company, BotLanguage language)
    {
        var announcement = company.Discovered && company.Items.Any(i => i.ChangeKind == VacancyChangeKind.New);

        var title = announcement
            ? $"🔎 {BotTexts.Get(TextKey.NotificationNewBoard, language)} · {Escape(company.Company)}"
            : $"{(company.Discovered ? "🔎" : "🏢")} {Escape(company.Company)}";

        var counts = new StringBuilder();

        foreach (var kind in KindOrder)
        {
            var count = company.Items.Count(i => i.ChangeKind == kind);

            if (count > 0)
                counts.Append($" · {KindGlyph(kind)} {count}");
        }

        var summary = $"<details><summary><b>{title}{counts}</b></summary>";
        var lines = company.Items.Select(i => $"<p>{RenderVacancy(i, language)}</p>").ToList();

        // A company is one block; only one that cannot fit a message at all is continued under a repeated header.
        var block = new StringBuilder(summary);
        var used = summary.Length + DetailsClose.Length;

        foreach (var line in lines)
        {
            if (used + line.Length > SafeLimit && block.Length > summary.Length)
            {
                yield return block.Append(DetailsClose).ToString();

                block = new StringBuilder(summary);
                used = summary.Length + DetailsClose.Length;
            }

            block.Append(line);
            used += line.Length;
        }

        if (block.Length > summary.Length)
            yield return block.Append(DetailsClose).ToString();
    }

    private string RenderVacancy(OutboxItem item, BotLanguage language)
    {
        var fresh = IsFresh(item);

        var title = RenderTitleLink(item);
        var geography = RenderGeography(item, language);
        var dates = RenderDate(PublishedAt(item), language) ?? RenderDate(item.Vacancy.FirstSeenAt, language);

        var line = $"{KindGlyph(item.ChangeKind)} {title}<br> {geography} · {dates}";

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
        .. items
            .OrderBy(i => Array.IndexOf(KindOrder, i.ChangeKind))
            .ThenByDescending(PublishedAt)
            .ThenBy(i => i.Vacancy.Title, StringComparer.OrdinalIgnoreCase)
    ];

    private static string RenderTitleLink(OutboxItem item) =>
        $"<a href=\"{Escape(item.Vacancy.Url)}\"><b>{Escape(item.Vacancy.Title)}</b></a>";

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

    /// <summary>The kind of a change in one character - a folded block mixes all three.</summary>
    private static string KindGlyph(VacancyChangeKind kind) =>
        kind switch
        {
            VacancyChangeKind.New => "🆕",
            VacancyChangeKind.Updated => "✏️",
            VacancyChangeKind.Closed => "❌",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };

    public static string Escape(string? text) =>
        WebUtility.HtmlEncode(text ?? string.Empty);

    /// <summary>One time window of one watchlist - the unit a delivered message is built from.</summary>
    private sealed record Section(
        DateTimeOffset From,
        DateTimeOffset To,
        string? Watchlist,
        IReadOnlyList<CompanyBatch> Companies);

    /// <summary>Everything that happened at one company inside a window.</summary>
    private sealed record CompanyBatch(string Company, bool Discovered, IReadOnlyList<OutboxItem> Items);
}
