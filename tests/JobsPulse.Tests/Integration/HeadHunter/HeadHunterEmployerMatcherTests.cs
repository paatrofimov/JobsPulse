using FluentAssertions;
using JobsPulse.Sources.HeadHunter.Infrastructure;
using JobsPulse.Sources.HeadHunter.Models;
using NUnit.Framework;

namespace JobsPulse.Tests.Integration.HeadHunter;

public sealed class HeadHunterEmployerMatcherTests
{
    /// <summary>The legal form, the quotes and the punctuation are not part of a company's identity.</summary>
    [TestCase("Яндекс", "ООО «Яндекс»")]
    [TestCase("Яндекс.Такси", "Яндекс Такси")]
    [TestCase("headhunter", "Head Hunter")]
    [TestCase("Ozon", "Ozon Group")]
    [TestCase("Ёж", "Еж")]
    public void Score_should_read_the_same_name_written_differently_as_exact(string query, string employer) =>
        HeadHunterEmployerMatcher.Score(query, employer).Should().Be(HeadHunterEmployerMatcher.ExactScore);

    /// <summary>
    /// A brand is what a user types and the catalog holds the fuller name, so this has to score high - requiring an
    /// exact name would answer 'nothing found' for most companies on the platform.
    /// </summary>
    [Test]
    public void Score_should_rank_a_longer_catalog_name_above_a_name_that_merely_shares_a_word()
    {
        var extended = HeadHunterEmployerMatcher.Score("Ozon", "Ozon Fintech");
        var shared = HeadHunterEmployerMatcher.Score("Альфа Банк", "Альфа Страхование");

        extended.Should().BeGreaterThan(shared);
        extended.Should().BeGreaterThan(50);
    }

    [Test]
    public void Score_should_be_zero_for_an_unrelated_employer() =>
        HeadHunterEmployerMatcher.Score("Яндекс", "Сбербанк").Should().Be(0);

    [Test]
    public void Rank_should_put_the_exact_name_first_however_the_search_ordered_its_results()
    {
        var ranked = HeadHunterEmployerMatcher.Rank(
            "Ozon",
            [
                Employer("1", "Ozon Fintech", 400),
                Employer("2", "Ozon", 100),
                Employer("3", "Ozon Банк", 50)
            ]);

        ranked[0].Employer.Id.Should().Be("2");
        ranked[0].Score.Should().Be(HeadHunterEmployerMatcher.ExactScore);
    }

    /// <summary>Among names that read the same, the record with more open vacancies is the parent one far more often.</summary>
    [Test]
    public void Rank_should_prefer_the_bigger_employer_among_equal_names()
    {
        var ranked = HeadHunterEmployerMatcher.Rank(
            "Яндекс",
            [
                Employer("1", "Яндекс", 12),
                Employer("2", "ООО «Яндекс»", 900)
            ]);

        ranked[0].Employer.Id.Should().Be("2");
        ranked.Should().OnlyContain(m => m.Score == HeadHunterEmployerMatcher.ExactScore);
    }

    [Test]
    public void Rank_should_drop_an_employer_the_catalog_gave_no_id()
    {
        var ranked = HeadHunterEmployerMatcher.Rank(
            "Ozon",
            [
                new EmployerItemDto { Id = null, Name = "Ozon" },
                Employer("2", "Ozon", 1)
            ]);

        ranked.Should().HaveCount(1);
        ranked[0].Employer.Id.Should().Be("2");
    }

    private static EmployerItemDto Employer(string id, string name, int openVacancies) =>
        new() { Id = id, Name = name, OpenVacancies = openVacancies };
}
