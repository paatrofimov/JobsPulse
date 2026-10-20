using System.Text.RegularExpressions;

namespace JobsPulse.Core.Infrastructure;

public static partial class LanguageRequirementDetector
{
    /// <summary>
    /// True when a sentence outside the optional sections demands a spoken language other than English - «fluent
    /// German», «French speaker», «Polish C1». A language wanted as a plus does not count.
    /// </summary>
    public static bool RequiresOther(string? description)
    {
        foreach (var sentence in DescriptionText.Sentences(OptionalSectionStripper.Strip(description)))
        {
            if (Optional().IsMatch(sentence))
                continue;

            if (Demand().IsMatch(sentence))
                return true;
        }

        return false;
    }

    [GeneratedRegex(@"\b(a plus|plus\b|nice to have|good to have|bonus|advantage|advantageous|desirable|an asset|beneficial|preferred|ideally|helpful|not required)", RegexOptions.IgnoreCase)]
    private static partial Regex Optional();

    [GeneratedRegex(@"\b(fluent|fluency|native|proficien\w*|command of|excellent|very good|good|strong|business|working|written and (spoken|verbal)|spoken and written)\W+(\w+\W+){0,3}?(german|french|dutch|polish|spanish|portuguese|italian|swedish|danish|norwegian|finnish|czech|hungarian|japanese|mandarin|chinese|korean|hebrew|arabic|turkish|romanian|greek|bulgarian|croatian|serbian|lithuanian|estonian|latvian|slovak|slovenian)\b|\b(german|french|dutch|polish|spanish|portuguese|italian|swedish|danish|norwegian|finnish|czech|hungarian|japanese|mandarin|chinese|korean|hebrew|arabic|turkish|romanian|greek)\W+(\w+\W+){0,2}?(speaking|speaker|is a must|is required|required|mandatory|fluency|c1|c2|b2|native|language skills)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Demand();
}
