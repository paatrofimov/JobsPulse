using JobsPulse.Core.Helpers;
using JobsPulse.Core.Model.Domain;
using NUnit.Framework;

namespace JobsPulse.Tests.Integration;

[TestFixture]
public abstract partial class IntegrationTestBase
{
    protected string VacancyToString(Vacancy
        vacancy)
    {
        return $"  {vacancy.Key} | {vacancy.Title} | " +
               $"{vacancy.Location ?? "-"} | Posted {vacancy.FirstSeenAt:u} | Updated {vacancy.UpdatedAt:u} | {vacancy.Offices.JoinStrings()} | {vacancy.Url}";
    }
}