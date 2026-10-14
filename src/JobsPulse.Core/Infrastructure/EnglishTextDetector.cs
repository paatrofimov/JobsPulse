using System.Net;
using System.Text.RegularExpressions;

namespace JobsPulse.Core.Infrastructure;

public static partial class EnglishTextDetector
{
    // Function words no other language spells the same way; «a», «in», «to», «de» are left out for that reason.
    private static readonly HashSet<string> FunctionWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "of", "with", "for", "you", "your", "we", "our", "will", "are", "is", "be", "this", "that",
        "on", "as", "or", "an", "at", "by", "from", "have", "has", "who", "can", "all", "their", "they", "it", "its",
        "which", "been", "would", "should", "into", "about"
    };

    // Measured on job ads: English text has 13% and more of these words, other Latin-script languages 5% and less.
    private const double MinFunctionWordShare = 0.08;

    private const int MinWords = 40;

    /// <summary>
    /// False only when the text is clearly in another language. A text too short to judge is taken as English, so a
    /// missing or tiny description never drops a vacancy.
    /// </summary>
    public static bool IsEnglish(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return true;

        text = WebUtility.HtmlDecode(WebUtility.HtmlDecode(text));
        text = Tag().Replace(text, " ");

        int latin = 0, other = 0;
        foreach (var c in text)
        {
            if (!char.IsLetter(c))
                continue;

            if (c <= 'ɏ')
                latin++;
            else
                other++;
        }

        // Cyrillic, CJK and the like - a CJK sentence is one «word» to the tokenizer, so letters are counted instead.
        // A fifth of them is already a text written mostly in that script: a CJK letter is worth a whole word.
        if (other * 4 > latin)
            return false;

        var words = LatinWord().Matches(text);
        if (words.Count < MinWords)
            return true;

        var function = words.Count(w => FunctionWords.Contains(w.Value));

        return function >= words.Count * MinFunctionWordShare;
    }

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tag();

    [GeneratedRegex(@"[A-Za-zÀ-ɏ]+")]
    private static partial Regex LatinWord();
}
