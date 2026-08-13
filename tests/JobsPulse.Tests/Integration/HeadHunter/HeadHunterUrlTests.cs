using FluentAssertions;
using JobsPulse.Sources.HeadHunter.Infrastructure;
using NUnit.Framework;

namespace JobsPulse.Tests.Integration.HeadHunter;

public sealed class HeadHunterUrlTests
{
    /// <summary>Every regional site and every city subdomain of one addresses the same catalog.</summary>
    [TestCase("https://hh.ru/employer/1740", "1740")]
    [TestCase("https://spb.hh.ru/employer/1740?from=vacancy", "1740")]
    [TestCase("https://hh.kz/employer/2180", "2180")]
    [TestCase("https://rabota.by/employer/3529", "3529")]
    [TestCase("https://api.hh.ru/employers/1740", "1740")]
    [TestCase("https://hh.ru/search/vacancy?employer_id=1740", "1740")]
    public void Parse_should_read_the_employer_id(string url, string expected)
    {
        var parts = HeadHunterUrl.Parse(url);

        parts.Should().NotBeNull();
        parts!.EmployerId.Should().Be(expected);
        parts.VacancyId.Should().BeNull();
    }

    /// <summary>A vacancy link names no employer - it is the resolver that trades one request for the employer behind it.</summary>
    [TestCase("https://hh.ru/vacancy/123456789", "123456789")]
    [TestCase("https://nn.hh.ru/vacancy/123456789?query=c%23", "123456789")]
    public void Parse_should_read_the_vacancy_id(string url, string expected)
    {
        var parts = HeadHunterUrl.Parse(url);

        parts.Should().NotBeNull();
        parts!.VacancyId.Should().Be(expected);
        parts.EmployerId.Should().BeNull();
    }

    /// <summary>Every resolver is asked about every url, so anything else has to be answered with null.</summary>
    [TestCase("https://hh.ru/employer/rating")]
    [TestCase("https://hh.ru/articles/123")]
    [TestCase("https://boards.greenhouse.io/nebius")]
    [TestCase("https://hhhh.ru/employer/1740")]
    [TestCase("not a url")]
    public void Parse_should_reject_anything_that_is_not_a_catalog_entity(string url) =>
        HeadHunterUrl.Parse(url).Should().BeNull();
}
