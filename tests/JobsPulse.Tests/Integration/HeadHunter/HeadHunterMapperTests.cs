using FluentAssertions;
using JobsPulse.Sources.HeadHunter.Infrastructure;
using JobsPulse.Sources.HeadHunter.Models;
using NUnit.Framework;

namespace JobsPulse.Tests.Integration.HeadHunter;

public sealed class HeadHunterMapperTests
{
    private readonly HeadHunterMapper mapper = new(TimeProvider.System);

    [Test]
    public void ToVacancy_should_map_the_employer_as_the_board()
    {
        var vacancy = mapper.ToVacancy(
            new VacancyItemDto
            {
                Id = "123",
                Name = "Senior .NET Developer",
                Area = new NamedDto { Id = "1", Name = "Москва" },
                Address = new VacancyAddressDto { City = "Санкт-Петербург" },
                AlternateUrl = "https://hh.ru/vacancy/123",
                PublishedAt = new DateTimeOffset(2026, 8, 10, 9, 0, 0, TimeSpan.Zero),
                CreatedAt = new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero),
                Snippet = new VacancySnippetDto { Requirement = "C#", Responsibility = "Писать код" }
            },
            "1740");

        vacancy.Should().NotBeNull();
        vacancy!.SourceId.Should().Be("headhunter");
        vacancy.BoardId.Should().Be("1740");
        vacancy.PostId.Should().Be("123");
        vacancy.Title.Should().Be("Senior .NET Developer");
        vacancy.Url.Should().Be("https://hh.ru/vacancy/123");
        // The address is where the job is; `area` is only the region the ad was published in.
        vacancy.Location.Should().Be("Санкт-Петербург");
        vacancy.Offices.Should().Equal("Санкт-Петербург");
        vacancy.FirstPublishedAt.Should().Be(new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero));
        vacancy.UpdatedAt.Should().Be(new DateTimeOffset(2026, 8, 10, 9, 0, 0, TimeSpan.Zero));
        vacancy.Description.Should().Be("Писать код\nC#");
    }

    /// <summary>
    /// The catalog exposes no requisition id, so nothing may be grouped - collapsing two ads of one employer that
    /// share a city would drop unrelated jobs.
    /// </summary>
    [Test]
    public void ToVacancy_should_leave_the_group_empty()
    {
        var vacancy = mapper.ToVacancy(new VacancyItemDto { Id = "1", Name = "Engineer" }, "1740");

        vacancy!.GroupId.Should().BeNull();
    }

    [TestCase("REMOTE", "Москва (remote)")]
    [TestCase("HYBRID", "Москва (hybrid)")]
    [TestCase("ON_SITE", "Москва")]
    public void ToVacancy_should_mark_the_work_format(string format, string expected)
    {
        var vacancy = mapper.ToVacancy(
            new VacancyItemDto
            {
                Id = "1",
                Name = "Engineer",
                Area = new NamedDto { Name = "Москва" },
                WorkFormat = [new NamedDto { Id = format }]
            },
            "1740");

        vacancy!.Location.Should().Be(expected);
    }

    [Test]
    public void ToVacancy_should_prefer_the_full_description_over_the_snippet()
    {
        var vacancy = mapper.ToVacancy(
            new VacancyItemDto
            {
                Id = "1",
                Name = "Engineer",
                Snippet = new VacancySnippetDto { Requirement = "C#" }
            },
            "1740",
            new VacancyDetailDto { Id = "1", Description = "<p>The whole ad</p>" });

        vacancy!.Description.Should().Be("<p>The whole ad</p>");
    }

    [Test]
    public void ToVacancy_should_fall_back_to_the_canonical_url()
    {
        var vacancy = mapper.ToVacancy(new VacancyItemDto { Id = "77", Name = "Engineer" }, "1740");

        vacancy!.Url.Should().Be("https://hh.ru/vacancy/77");
    }

    /// <summary>A vacancy with no id cannot be followed across polls, so it is dropped rather than given one.</summary>
    [Test]
    public void ToVacancy_should_drop_a_vacancy_without_an_id() =>
        mapper.ToVacancy(new VacancyItemDto { Name = "Engineer" }, "1740").Should().BeNull();
}
