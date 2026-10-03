using System.Globalization;
using JobsPulse.Core.Model.Infrastructure;

namespace JobsPulse.Sinks.Telegram.Infrastructure;

/// <summary>
/// <c>t.me/&lt;bot&gt;?start=&lt;payload&gt;</c> links: a tap sends <c>/start &lt;payload&gt;</c> to the bot, which is
/// how a plain link inside a message opens a screen. A payload is <c>c&lt;entryId&gt;</c> - the vacancies of a company.
/// </summary>
public static class DeepLinks
{
    private const string CompanyPrefix = "c";

    public static string Company(string botUsername, long entryId) =>
        $"https://t.me/{botUsername}?start={CompanyPrefix}{entryId.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>
    /// Company name to its link inside one watchlist: the first enabled entry under that name, null for a company
    /// that is not watched (or when the bot username is unknown).
    /// </summary>
    public static Func<string, string?> Companies(Watchlist watchlist, string? botUsername)
    {
        if (string.IsNullOrEmpty(botUsername))
            return _ => null;

        var entries = watchlist.Entries
            .Where(e => e.Enabled)
            .GroupBy(e => e.CompanyName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

        return name => entries.TryGetValue(name, out var id)
            ? Company(botUsername, id)
            : null;
    }

    public static bool TryParseCompany(string? payload, out long entryId)
    {
        entryId = 0;

        return payload is not null
               && payload.StartsWith(CompanyPrefix, StringComparison.Ordinal)
               && long.TryParse(payload[CompanyPrefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out entryId);
    }
}
