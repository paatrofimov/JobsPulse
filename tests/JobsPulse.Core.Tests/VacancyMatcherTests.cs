using JobsPulse.Core.Model;
using JobsPulse.Core.Pipeline;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace JobsPulse.Core.Tests;

public sealed class VacancyMatcherTests
{
    private readonly VacancyMatcher _matcher = new(new FakeTimeProvider(TestData.Now));

    [Fact]
    public void Empty_filter_matches_everything()
    {
        Assert.True(_matcher.Matches(TestData.Vacancy(), FilterSpec.MatchAll));
    }

    [Fact]
    public void TitleNoneOf_wins_over_TitleAnyOf()
    {
        // Исключение — жёсткое: «backend» подходит, но «intern» перебивает.
        var filter = new FilterSpec
        {
            TitleAnyOf = ["backend"],
            TitleNoneOf = ["intern"]
        };

        Assert.False(_matcher.Matches(TestData.Vacancy(title: "Backend Intern"), filter));
    }

    [Fact]
    public void LocationAnyOf_also_checks_offices()
    {
        var filter = new FilterSpec { LocationAnyOf = ["Berlin"] };
        var vacancy = TestData.Vacancy(location: "Hybrid") with { Offices = ["Berlin"] };

        Assert.True(_matcher.Matches(vacancy, filter));
    }

    [Fact]
    public void PostedWithinDays_cuts_off_old_vacancies()
    {
        var filter = new FilterSpec { PostedWithinDays = 30 };

        Assert.False(_matcher.Matches(TestData.Vacancy(firstPublished: TestData.Now.AddDays(-90)), filter));
        Assert.True(_matcher.Matches(TestData.Vacancy(firstPublished: TestData.Now.AddDays(-5)), filter));
    }

    [Fact]
    public void Broken_regex_does_not_throw()
    {
        var filter = new FilterSpec { TitleAnyOf = ["([unclosed"], MatchMode = MatchMode.Regex };

        Assert.False(_matcher.Matches(TestData.Vacancy(), filter));
    }
}
