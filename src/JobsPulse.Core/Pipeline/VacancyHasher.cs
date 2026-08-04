using System.Security.Cryptography;
using System.Text;
using JobsPulse.Core.Model;

namespace JobsPulse.Core.Pipeline;

/// <summary>
/// Контент-хеш вакансии.
///
/// Зачем: у Greenhouse updated_at дёргается от любой правки, включая косметическую.
/// Сравнивать по нему — значит слать «вакансия обновлена» на каждую опечатку в описании.
/// Хешируем только то, что реально важно пользователю.
/// </summary>
public static class VacancyHasher
{
    /// <summary>Unit separator — не встречается в текстах вакансий, поэтому безопасен как разделитель полей.</summary>
    private const char Separator = '\u001F';

    public static string Compute(Vacancy v)
    {
        var sb = new StringBuilder();
        Append(sb, v.Title);
        Append(sb, v.Location);
        Append(sb, v.Url);
        foreach (var d in v.Departments.OrderBy(x => x, StringComparer.Ordinal)) Append(sb, d);
        foreach (var o in v.Offices.OrderBy(x => x, StringComparer.Ordinal)) Append(sb, o);

        return Hash(sb.ToString());
    }

    /// <summary>
    /// Хеш фильтра. Если фильтр изменился, набор подходящих вакансий меняется,
    /// и запись надо пересеять молча — иначе прилетит пачка «новых» вакансий, которые просто раньше отсеивались.
    /// </summary>
    public static string ComputeFilterHash(FilterSpec f)
    {
        var sb = new StringBuilder();
        sb.Append(f.MatchMode).Append('|').Append(f.PostedWithinDays).Append('|');

        foreach (var list in new[] { f.TitleAnyOf, f.TitleNoneOf, f.LocationAnyOf, f.LocationNoneOf, f.DepartmentAnyOf })
        {
            foreach (var s in list.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                Append(sb, s.ToLowerInvariant());
            sb.Append(';');
        }

        return Hash(sb.ToString());
    }

    private static void Append(StringBuilder sb, string? value) =>
        sb.Append(value ?? string.Empty).Append(Separator);

    private static string Hash(string input) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input)))[..32];
}
