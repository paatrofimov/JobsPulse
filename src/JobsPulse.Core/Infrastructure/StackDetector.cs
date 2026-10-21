using System.Text.RegularExpressions;
using JobsPulse.Core.Model.Infrastructure;

namespace JobsPulse.Core.Infrastructure;

public static partial class StackDetector
{
    /// <summary>
    /// The stack is primary when the title names it, or when a sentence of the description outside the optional
    /// sections names it without calling it a plus - «C#, Java or Go» counts, «C# is a plus» does not.
    /// The stack is the environment when the title names it, or such a sentence names it without offering another
    /// language as an alternative - «.NET 8, ASP.NET Core, Python» counts, «Python, Java or C#» does not.
    /// Another language is required when a sentence asks for experience with it, does not offer the stack instead
    /// and is not about scripting.
    /// </summary>
    public static StackReading Read(string? title, string? description, Func<string, bool> isStack)
    {
        var primary = !string.IsNullOrEmpty(title) && isStack($" {title} ");
        var core = primary;
        var otherRequired = false;

        foreach (var sentence in DescriptionText.Sentences(OptionalSectionStripper.Strip(description)))
        {
            if (Optional().IsMatch(sentence))
                continue;

            if (isStack($" {sentence} "))
            {
                primary = true;

                if (!core && !(OtherLanguage().IsMatch(sentence) && Choice().IsMatch(sentence)))
                    core = true;

                continue;
            }

            if (!otherRequired && OtherLanguage().IsMatch(sentence) && Requirement().IsMatch(sentence)
                && !Scripting().IsMatch(sentence))
                otherRequired = true;
        }

        return new StackReading(primary, core, otherRequired);
    }

    [GeneratedRegex(@"\b(a plus|plus\b|nice to have|nice-to-have|good to have|great to have|bonus|advantage|advantageous|desirable|familiarity|exposure|beneficial|an asset|not required|not mandatory|helpful|would be great)", RegexOptions.IgnoreCase)]
    private static partial Regex Optional();

    // «Go» only capitalized and not as a verb; the rest by word, «Java» but not «JavaScript».
    [GeneratedRegex(@"(?i:\bjava\b(?!\s*script)|\bpython\b|\bgolang\b|\bnode\.?js\b|\bruby\b|\bphp\b|c\+\+|\bkotlin\b|\bscala\b|\brust\b|\belixir\b|\berlang\b|\bclojure\b|\bperl\b)|(?<=[\s(,/])Go\b(?![- ](to|beyond|live|above)\b)")]
    private static partial Regex OtherLanguage();

    [GeneratedRegex(@"\bexperience\b|\bexperienced\b|\byears\b|proficien|\bstrong\b|\bexpert|\bsolid\b|\bdeep\b|\bproduction\b|hands-on|\bprofessional\b|\bcommercial\b|\bmust\b|\brequired\b|\badvanced\b|\bfluen|\bmastery\b|\bknowledge\b", RegexOptions.IgnoreCase)]
    private static partial Regex Requirement();

    // A language list the candidate picks one from: «C#, Java or Go», «at least one of», «such as», «e.g.», «etc.».
    [GeneratedRegex(@"\bor\b|\bat least\b|\bone or more\b|\bsuch as\b|\be\.g\.|\bfor example\b|\bsimilar\b|\betc\b|\bany\b|\bequivalent\b", RegexOptions.IgnoreCase)]
    private static partial Regex Choice();

    [GeneratedRegex(@"script(ing|s)?\b|\bbash\b|\bpowershell\b", RegexOptions.IgnoreCase)]
    private static partial Regex Scripting();
}
