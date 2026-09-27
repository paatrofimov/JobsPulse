using JobsPulse.Sinks.Telegram.Models;

namespace JobsPulse.Sinks.Telegram.Infrastructure;

/// <summary>
/// One answer to a filter field prompt, applied to the field. The sign of each word says which list it belongs to,
/// so a field needs one button, not a «wanted» and an «excluded» one:
/// - <c>backend, -intern</c> adds: a word without a sign (or with <c>+</c>) is wanted, one with <c>-</c> is excluded,
///   and a word already in the other list moves;
/// - <c>= backend, -intern</c> replaces the whole field - the form <see cref="Render"/> shows the field in, so it can
///   be copied, edited and sent back;
/// - a lone <c>-</c> clears the field.
/// </summary>
public static class FilterFieldEdit
{
    public static FilterField Apply(FilterField current, string input)
    {
        var text = input.Trim();

        if (text is "-" or "—")
            return FilterField.Empty;

        var replace = text.StartsWith('=');
        if (replace)
            text = text[1..];

        var wanted = replace ? [] : current.Wanted.ToList();
        var excluded = replace ? [] : current.Excluded.ToList();

        foreach (var item in text.Split([',', '\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // Only a leading sign counts - `on-site` is one word, `-on-site` excludes it.
            var (target, other, word) = item[0] switch
            {
                '-' or '—' => (excluded, wanted, item[1..].Trim()),
                '+' => (wanted, excluded, item[1..].Trim()),
                _ => (wanted, excluded, item)
            };

            if (word.Length == 0)
                continue;

            other.RemoveAll(w => string.Equals(w, word, StringComparison.OrdinalIgnoreCase));

            if (!target.Contains(word, StringComparer.OrdinalIgnoreCase))
                target.Add(word);
        }

        return new FilterField(wanted, excluded);
    }

    /// <summary>The field as a replacing answer: <c>= backend, sre, -intern</c>.</summary>
    public static string Render(FilterField field) =>
        "= " + string.Join(", ", field.Wanted.Concat(field.Excluded.Select(w => "-" + w)));
}
