namespace JobsPulse.Sinks.Telegram.Infrastructure;

/// <summary>
/// One answer to a filter rule prompt applied to the words the rule already holds. A plain list replaces them, as it
/// always did; a leading <c>+</c> adds to them and a leading <c>-</c> followed by words removes those - so extending a
/// long list no longer means typing it out again. A lone <c>-</c> still clears the rule.
/// </summary>
public static class FilterListEdit
{
    public static IReadOnlyList<string> Apply(IReadOnlyList<string> current, string input)
    {
        var text = input.Trim();

        if (text.StartsWith('+'))
        {
            return
            [
                .. current
                    .Concat(BotFormatter.ParseList(text[1..]))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
            ];
        }

        // `-` alone keeps meaning «clear», so removal needs at least one word after it.
        if (text.Length > 1 && text[0] is '-' or '—')
        {
            var removed = BotFormatter.ParseList(text[1..]).ToHashSet(StringComparer.OrdinalIgnoreCase);

            return [.. current.Where(word => !removed.Contains(word))];
        }

        return BotFormatter.ParseList(text);
    }
}
