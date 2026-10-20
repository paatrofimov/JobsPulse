using System.Text.RegularExpressions;

namespace JobsPulse.Core.Infrastructure;

public static partial class WorkAuthorizationDetector
{
    /// <summary>
    /// True when the vacancy wants a candidate who already may work there - a work authorization, citizenship,
    /// residency or a clearance, or a refusal to sponsor - and offers no help with a visa or a relocation anywhere
    /// in its text. Equal opportunity sentences («without regard to citizenship») are not read.
    /// </summary>
    public static bool RequiresExisting(string? description)
    {
        var required = false;
        var offered = false;

        foreach (var sentence in DescriptionText.Sentences(description))
        {
            if (EqualOpportunity().IsMatch(sentence))
                continue;

            if (!required && Requirement().IsMatch(sentence))
                required = true;

            if (!offered && Offer().Matches(sentence).Any(m => !IsNegated(sentence, m.Index)))
                offered = true;
        }

        return required && !offered;
    }

    private static bool IsNegated(string sentence, int index)
    {
        var from = Math.Max(0, index - 40);

        return Negation().IsMatch(sentence[from..index]);
    }

    [GeneratedRegex(@"regard to|regardless of|discriminat|protected (veteran|characteristic|status|class)|equal (employment )?opportunit|marital|national origin", RegexOptions.IgnoreCase)]
    private static partial Regex EqualOpportunity();

    [GeneratedRegex(@"(\bnot\b|\bunable to\b|\bcannot\b|n't\b|\bno longer\b|\bnor\b)\W+(\w+\W+){0,4}?(sponsor|sponsorship|visa)|\bno (work )?visa sponsorship|sponsorship\?:?\s*no\b|sponsorship (is not|will not|isn't)|without (the need for |requiring |any )?(visa |employer |immigration )?sponsorship|not eligible for (employment |work )?(visa )?sponsorship|(now|presently),? or in the future|\bh-?1-?b\b|\bstem opt\b|\btn visa|(valid|unrestricted|current) (and unrestricted )?(u\.?s\.? |us |uk |eu )?work (authori[sz]ation|permit)|(authori[sz]ed|eligible|entitled|permitted) to (legally )?work (full[- ]time )?(in|within|for|at)\b|legally (eligible|authori[sz]ed) to work|right to work (in|within)|indefinite right to work|\bwork rights\b|permanent basis without|\bcitizens? (only|or (lawful )?permanent)|limited to (u\.?s\.? )?citizens|must (be|hold) (a |an )?([a-z.]+ )?(citizen|passport)|\bu\.?s\.? (citizen|person)|\bus citizen|permanent resident|green card|security clearance|secret clearance|active clearance|clearance (is )?required(?!\?\s*no)|\bts/sci\b|\(itar\)|\bitar\b|arms regulations", RegexOptions.IgnoreCase)]
    private static partial Regex Requirement();

    [GeneratedRegex(@"visa sponsorship (is )?(available|provided|offered|possible)|sponsorship (is )?available|\bwe (can |will |do )?sponsor\b|(will|happy to|willing to|able to) (consider )?sponsor|consider sponsoring|(offer|provide|support)s? (\w+ )?(visa|work permit|immigration) (sponsorship|support)|visa (support|assistance)|require visa sponsorship\. we|support candidates who require|relocation (package|support|bonus|allowance|assistance (is )?(available|provided|offered))|relocation assistance provided: yes|relocation eligible: yes|eligible for relocation|help (you )?(with )?relocat|sponsorship\?:? yes", RegexOptions.IgnoreCase)]
    private static partial Regex Offer();

    [GeneratedRegex(@"\b(not|no|unable|cannot|without|nor)\b|n't\b", RegexOptions.IgnoreCase)]
    private static partial Regex Negation();
}
