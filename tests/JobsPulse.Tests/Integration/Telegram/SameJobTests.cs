using FluentAssertions;
using JobsPulse.Core.Model.Domain;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Sinks.Telegram.Infrastructure;
using NUnit.Framework;

namespace JobsPulse.Tests.Integration.Telegram;

/// <summary>One job posted for several cities is one line, and every city still links to its own posting.</summary>
public sealed class SameJobTests
{
    private static Vacancy Posting(string postId, string title, string? location, string board = "acme") => new()
    {
        SourceId = "greenhouse",
        BoardId = board,
        PostId = postId,
        Title = title,
        Location = location,
        Url = $"https://example.com/{postId}"
    };

    [Test]
    public void Group_should_join_the_same_title_of_one_board_in_order()
    {
        Vacancy[] postings =
        [
            Posting("1", "Senior Engineer", "London"),
            Posting("2", "Backend Engineer", "Berlin"),
            Posting("3", "senior engineer ", "Vancouver"),
            Posting("4", "Senior Engineer", "Paris", board: "other")
        ];

        var groups = SameJob.Group(postings, v => v);

        groups.Select(g => g.Select(v => v.PostId)).Should().BeEquivalentTo(
            [new[] { "1", "3" }, ["2"], ["4"]], o => o.WithStrictOrdering());
    }

    [Test]
    public void Places_should_link_every_city_once()
    {
        var places = SameJob.Places(
            [Posting("1", "Engineer", "London"), Posting("2", "Engineer", "Vancouver"), Posting("3", "Engineer", "london")],
            BotLanguage.English);

        places.Should().Be(
            "<a href=\"https://example.com/1\">London</a> · <a href=\"https://example.com/2\">Vancouver</a>");
    }

    [Test]
    public void Places_should_keep_a_single_posting_as_text()
    {
        SameJob.Places([Posting("1", "Engineer", "London & Remote")], BotLanguage.English).Should().Be("London &amp; Remote");
    }
}
