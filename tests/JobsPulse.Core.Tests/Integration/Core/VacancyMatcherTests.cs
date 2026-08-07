using JobsPulse.Core.Helpers;
using JobsPulse.Core.Model.Domain;
using JobsPulse.Core.Model.Infrastructure;
using NUnit.Framework;

namespace JobsPulse.Core.Tests.Integration.Core;

public sealed class VacancyMatcherTests : IntegrationTestBase
{
    public static IEnumerable<FilterSpec> Filters =>
    [
        new() { TitleAnyOf = ["Senior", "Software", "Engineer"], DescriptionAnyOf = [".net", "c#", "dotnet"], MatchMode = FilterMatchMode.Substring },
        new() { TitleAnyOf = ["Senior", "Software", "Engineer"], DescriptionAnyOf = ["performance issues"], MatchMode = FilterMatchMode.Substring },
    ];

    [TestCaseSource(nameof(Filters))]
    public async Task Matches_should_return_matches(FilterSpec filter)
    {
        var sourceTarget = new SourceTarget() { SourceId = "greenhouse", BoardId = "nebius", IncludeDescriptions = true };
        var result = await FetchRealVacanciesAsync(sourceTarget);

        var hits = new List<Vacancy>();
        foreach (var vacancy in result.Vacancies)
        {
            if (VacancyMatcher.Matches(vacancy, filter))
                hits.Add(vacancy);
        }

        TestContext.Progress.WriteLine($"Hits: {hits.Count}\n{hits.Select(VacancyToString).JoinStrings("\n\n")}");
    }
}