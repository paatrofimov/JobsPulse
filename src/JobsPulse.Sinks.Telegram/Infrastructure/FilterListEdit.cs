namespace JobsPulse.Sinks.Telegram.Infrastructure;

/// <summary>
/// One answer to a filter rule prompt applied to the words the rule already holds. A plain list replaces them, as it
/// always did; a leading <c>+</c> adds to them, so extending a long list no longer means typing it out again. A lone
/// <c>-</c> still clears the rule.
///
/// A leading <c>-</c> followed by words adds too. It once meant «remove these», and an answer to the
/// «excluded words» prompt like <c>- Intern, Research</c> - obviously «exclude these as well» - silently removed
/// words that were not there and changed nothing.
/// </summary>
public static class FilterListEdit
{
    public static IReadOnlyList<string> Apply(IReadOnlyList<string> current, string input)
    {
        var text = input.Trim();

        // `-` alone keeps meaning «clear», so a sign only means «add» with at least one word after it.
        if (text.Length > 1 && text[0] is '+' or '-' or '—')
        {
            return
            [
                .. current
                    .Concat(BotFormatter.ParseList(text[1..]))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
            ];
        }

        return BotFormatter.ParseList(text);
    }
}
