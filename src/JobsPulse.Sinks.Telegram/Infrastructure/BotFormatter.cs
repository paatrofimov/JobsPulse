using System.Text;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Sinks.Telegram.Infrastructure.Localization;
using JobsPulse.Sinks.Telegram.Models;

namespace JobsPulse.Sinks.Telegram.Infrastructure;

/// <summary>
/// Rendering shared by the screens: the company status glyph, the owner label and the human wording of a filter.
/// One place, so «active / disabled / worked through» looks the same in every list.
/// </summary>
public static class BotFormatter
{
    public const string ActiveGlyph = "▶️";
    public const string DisabledGlyph = "⏸";
    public const string WorkedGlyph = "✅";
    public const string DiscoveryGlyph = "🔎";

    /// <summary>Worked through wins over active: the point of the mark is to stand out in a long list.</summary>
    public static string EntryGlyph(WatchlistEntry entry)
    {
        if (!entry.Enabled)
            return DisabledGlyph;

        return entry.IsWorked ? WorkedGlyph : ActiveGlyph;
    }

    public static string EntryStatus(WatchlistEntry entry, BotLanguage language)
    {
        if (!entry.Enabled)
            return BotTexts.Get(TextKey.CompanyStatusDisabled, language);

        return entry.IsWorked
            ? BotTexts.Get(TextKey.CompanyStatusWorked, language)
            : BotTexts.Get(TextKey.CompanyStatusActive, language);
    }

    /// <summary>Button label of one company: glyph, name and the discovery mark when it was found automatically.</summary>
    public static string EntryButton(WatchlistEntry entry)
    {
        var discovered = entry.Origin == BoardOrigin.Discovery ? $" {DiscoveryGlyph}" : string.Empty;

        return $"{EntryGlyph(entry)} {entry.CompanyName}{discovered}";
    }

    public static string OwnerLabel(BotContext ctx, Watchlist watchlist, IReadOnlyDictionary<long, BotUser> owners)
    {
        if (watchlist.OwnerUserId is null)
            return BotTexts.Get(TextKey.WatchlistOwnerSystem, ctx.Language);

        if (watchlist.OwnerUserId == ctx.UserId)
            return BotTexts.Get(TextKey.WatchlistOwnerYou, ctx.Language);

        var owner = owners.GetValueOrDefault(watchlist.OwnerUserId.Value);

        return string.IsNullOrWhiteSpace(owner?.DisplayName)
            ? BotTexts.Get(TextKey.WatchlistOwnerOther, ctx.Language)
            : owner.DisplayName;
    }

    /// <summary>
    /// The filter in words rather than json - the user never sees `FilterSpec.ToString`, which is a debug rendering.
    /// </summary>
    public static string Filter(FilterSpec filter, BotLanguage language)
    {
        if (filter.IsEmpty)
            return BotTexts.Get(TextKey.FilterEmpty, language);

        var sb = new StringBuilder();

        Append(sb, TextKey.FilterKeywords, filter.TitleAnyOf, language);
        Append(sb, TextKey.FilterExcluded, filter.TitleNoneOf, language);
        Append(sb, TextKey.FilterLocations, filter.LocationAnyOf, language);
        Append(sb, TextKey.FilterLocationsExcluded, filter.LocationNoneOf, language);
        Append(sb, TextKey.FilterDescription, filter.DescriptionAnyOf, language);
        Append(sb, TextKey.FilterDescriptionExcluded, filter.DescriptionNoneOf, language);

        if (filter.PostedWithinDays is { } days)
        {
            sb.Append(BotTexts.Get(TextKey.FilterFreshness, language))
                .Append(": ")
                .Append(BotTexts.Get(TextKey.FilterDays, language, days))
                .Append("<br>");
        }

        return sb.ToString();
    }

    private static void Append(StringBuilder sb, TextKey label, IReadOnlyList<string> values, BotLanguage language)
    {
        if (values.Count == 0)
            return;

        sb.Append(BotTexts.Get(label, language))
            .Append(": <code>")
            .Append(MessageFormatter.Escape(string.Join(", ", values)))
            .Append("</code><br>");
    }

    /// <summary>Splits a comma or newline separated answer into filter values. `-` means «clear».</summary>
    public static IReadOnlyList<string> ParseList(string input)
    {
        if (input.Trim() is "-" or "—")
            return [];

        return
        [
            .. input
                .Split([',', '\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(x => x.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
        ];
    }
}
