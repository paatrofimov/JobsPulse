using System.Text;

namespace JobsPulse.Core.Helpers;

public static class StringExtensions
{
    public static string JoinStrings(this IEnumerable<string> source, string separator = ", ")
    {
        return string.Join(separator, source);
    }

    public static void AppendList(this StringBuilder sb, string name, IReadOnlyList<string> list)
    {
        if (list.Count > 0)
            sb.Append($", {name}: {list.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).Select(x => x.ToLowerInvariant()).JoinStrings("|")}");
    }
}