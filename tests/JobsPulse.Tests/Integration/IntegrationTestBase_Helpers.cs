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
               $"{vacancy.Location ?? "-"} | Posted {vacancy.FirstPublished:u} | Updated {vacancy.UpdatedAt:u} | {vacancy.Departments.JoinStrings()} | {vacancy.Offices.JoinStrings()} | {vacancy.Url}";
    }
}