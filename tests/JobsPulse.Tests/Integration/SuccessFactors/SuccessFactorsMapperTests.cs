using FluentAssertions;
using JobsPulse.Sources.SuccessFactors.Infrastructure;
using JobsPulse.Sources.SuccessFactors.Models;
using NUnit.Framework;

namespace JobsPulse.Tests.Integration.SuccessFactors;

public sealed class SuccessFactorsMapperTests
{
    private static readonly SuccessFactorsBoardConfig Board = new() { Domain = "jobs.sap.com" };

    private readonly SuccessFactorsMapper mapper = new();

    [Test]
    public void ToVacancy_should_take_the_location_off_the_title()
    {
        var vacancy = mapper.ToVacancy(
            new JobFeedItemDto
            {
                Id = "1419687233",
                Title = "Senior UX Designer - Design Systems (Prague 5, CZ, 158 00)",
                Location = "Prague 5, CZ, 158 00",
                Link = "https://jobs.sap.com/job/Prague-5-Senior-UX-Designer/1419687233/"
            },
            Board);

        vacancy.Should().NotBeNull();
        vacancy!.Title.Should().Be("Senior UX Designer - Design Systems");
        vacancy.Location.Should().Be("Prague 5, CZ, 158 00");
        vacancy.PostId.Should().Be("1419687233");
        vacancy.BoardId.Should().Be("jobs.sap.com");
    }

    /// <summary>
    /// Brackets at the end of a title are ordinary - German postings end in '(m/w/d)' - so only the item's own
    /// location may be taken off, and only when it matches exactly.
    /// </summary>
    [TestCase("Softwareentwickler (m/w/d)", "Walldorf, DE", "Softwareentwickler (m/w/d)")]
    [TestCase("Engineer (Berlin)", "Berlin, DE", "Engineer (Berlin)")]
    [TestCase("Engineer", "Berlin, DE", "Engineer")]
    [TestCase("Engineer (Berlin, DE)", null, "Engineer (Berlin, DE)")]
    public void ToVacancy_should_only_strip_an_exact_location_suffix(string title, string? location, string expected)
    {
        var vacancy = mapper.ToVacancy(
            new JobFeedItemDto { Id = "1", Title = title, Location = location, Link = "https://jobs.sap.com/job/x/1/" },
            Board);

        vacancy!.Title.Should().Be(expected);
    }

    [Test]
    public void ToVacancy_should_fall_back_to_the_id_in_the_url_when_the_feed_carries_none()
    {
        var vacancy = mapper.ToVacancy(
            new JobFeedItemDto { Title = "Engineer", Link = "https://jobs.sap.com/job/Berlin-Engineer/1404567933/" },
            Board);

        vacancy!.PostId.Should().Be("1404567933");
    }

    /// <summary>An item with no identity anywhere cannot be followed across polls, so it is dropped rather than given one.</summary>
    [Test]
    public void ToVacancy_should_drop_an_item_with_no_id()
    {
        mapper.ToVacancy(new JobFeedItemDto { Title = "Engineer", Link = "https://jobs.sap.com/talentcommunity/" }, Board)
            .Should().BeNull();
    }

    /// <summary>
    /// Neither date is mapped - the feed carries no publication date and the expiry it does carry is not one. A date
    /// invented from it would rewrite the content hash on the sites' own refresh schedule.
    /// </summary>
    [Test]
    public void ToVacancy_should_leave_both_dates_empty()
    {
        var vacancy = mapper.ToVacancy(
            new JobFeedItemDto
            {
                Id = "1",
                Title = "Engineer",
                Link = "https://jobs.sap.com/job/x/1/",
                ExpirationDate = "2026-09-11"
            },
            Board);

        vacancy!.FirstPublishedAt.Should().BeNull();
        vacancy.UpdatedAt.Should().BeNull();
        vacancy.GroupId.Should().BeNull();
    }
}
