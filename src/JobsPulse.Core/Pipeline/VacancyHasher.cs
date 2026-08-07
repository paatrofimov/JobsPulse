using System.Security.Cryptography;
using System.Text;
using JobsPulse.Core.Model.Domain;
using JobsPulse.Core.Model.Domain.Extensions;
using JobsPulse.Core.Model.Infrastructure;

namespace JobsPulse.Core.Pipeline;

public static class VacancyHasher
{
    public static string Compute(Vacancy v)
    {
        return Hash(v.ToStringForHash());
    }

    public static string ComputeFilterHash(FilterSpec f)
    {
        return Hash(f.ToStringForHash());
    }

    private static string Hash(string input) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input)))[..32];
}