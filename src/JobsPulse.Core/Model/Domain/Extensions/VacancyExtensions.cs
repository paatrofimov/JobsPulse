using System.Text;
using JobsPulse.Core.Helpers;
using JobsPulse.Core.Model.Infrastructure;

namespace JobsPulse.Core.Model.Domain.Extensions;

public static class VacancyExtensions
{
    public static string ToStringForHash(this Vacancy vacancy)
    {
        var sb = new StringBuilder();

        sb.Append(vacancy.Title);
        sb.Append(vacancy.Location);
        sb.Append(vacancy.Url);
        sb.AppendList(nameof(vacancy.Offices), vacancy.Offices);

        return sb.ToString();
    }

    public static string ToDedupKey(this Vacancy vacancy, VacancyChangeKind changeKind, string contentHash)
    {
        return $"{vacancy.Key}|{changeKind}|{contentHash}";
    }
}