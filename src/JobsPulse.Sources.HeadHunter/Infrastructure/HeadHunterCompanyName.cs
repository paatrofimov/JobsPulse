using System.Globalization;
using System.Text;

namespace JobsPulse.Sources.HeadHunter.Infrastructure;

/// <summary>
/// Company name to something two names can be compared as. Deliberately not a slug guesser: nothing here has to
/// address a board - the board id is an employer id the catalog hands out - so this only has to make «Яндекс Такси»,
/// «ООО "Яндекс.Такси"» and «yandex taxi» read the same way.
/// </summary>
public static class HeadHunterCompanyName
{
    /// <summary>
    /// Legal forms and marketing words that carry no identity. Dropped as whole tokens only, so a company actually
    /// called 'Group' keeps its name.
    /// </summary>
    private static readonly HashSet<string> NoiseTokens = new(StringComparer.Ordinal)
    {
        "ооо", "оао", "зао", "пао", "нао", "ао", "ип", "чуп", "одо", "тоо", "фгуп", "гуп", "мкк", "нко",
        "группа", "компания", "холдинг", "корпорация", "концерн",
        "llc", "ltd", "inc", "corp", "co", "plc", "gmbh", "ag", "bv", "sa", "srl", "oy", "ab",
        "group", "company", "holding", "holdings", "corporation", "labs", "technologies", "tech"
    };

    /// <summary>Lowercase, unaccented, punctuation collapsed to single spaces. 'ё' folds to 'е' - hh spells both.</summary>
    public static string Normalize(string name)
    {
        var decomposed = name.Trim().ToLowerInvariant().Replace('ё', 'е').Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);

        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsLetterOrDigit(ch))
                sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != ' ')
                sb.Append(' ');
        }

        return sb.ToString().Trim();
    }

    /// <summary>The identity-carrying words of a name, in the order they were written.</summary>
    public static IReadOnlyList<string> Tokens(string normalized)
    {
        var tokens = normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => !NoiseTokens.Contains(t))
            .ToList();

        // A name made of nothing but noise ('ООО Компания') still has to compare as something.
        return tokens.Count > 0
            ? tokens
            : normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// The tokens glued together, which is what makes 'head hunter' and 'headhunter' the same name and 'Яндекс.Такси'
    /// the same as 'Яндекс Такси'.
    /// </summary>
    public static string Compact(IReadOnlyList<string> tokens) => string.Concat(tokens);
}
