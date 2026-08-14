using System.Collections.Concurrent;
using FluentAssertions;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Sinks.Telegram.Infrastructure;
using NUnit.Framework;

namespace JobsPulse.Tests.Integration.Telegram;

/// <summary>
/// Ordering, source grouping and the name lookup of the company list. The lookup is what replaced a button per
/// company, so «which company did the user mean» is now a rule worth pinning down.
/// </summary>
public sealed class CompanyListTests
{
    [Test]
    public void Order_should_put_sources_together_and_active_companies_first()
    {
        var ordered = CompanyList.Order(
        [
            Entry(1, "lever", "Zeta"),
            Entry(2, "greenhouse", "beta", enabled: false),
            Entry(3, "greenhouse", "Alpha", origin: BoardOrigin.Discovery),
            Entry(4, "greenhouse", "Gamma")
        ], new ConcurrentDictionary<string, int>());

        ordered.Select(e => e.CompanyName).Should().Equal("Gamma", "Alpha", "beta", "Zeta");
    }

    [Test]
    public void GroupBySource_should_keep_the_order_and_one_group_per_source()
    {
        var groups = CompanyList.GroupBySource(CompanyList.Order(
        [
            Entry(1, "lever", "Zeta"),
            Entry(2, "greenhouse", "Alpha"),
            Entry(3, "greenhouse", "Gamma")
        ], new ConcurrentDictionary<string, int>()));

        groups.Select(g => g.Label).Should().Equal("greenhouse", "lever");
        groups[0].Entries.Select(e => e.CompanyName).Should().Equal("Alpha", "Gamma");
        groups[1].Entries.Should().HaveCount(1);
    }

    [Test]
    public void Find_should_match_a_name_case_insensitively()
    {
        var entries = new[] { Entry(1, "greenhouse", "Nebius AI"), Entry(2, "lever", "Wildberries") };

        CompanyList.Find(entries, "  wildBERRIES ").Should().ContainSingle().Which.Id.Should().Be(2);
    }

    /// <summary>Otherwise a company whose name is a prefix of another one is unreachable by typing it in full.</summary>
    [Test]
    public void Find_should_prefer_an_exact_match_over_a_containing_one()
    {
        var entries = new[] { Entry(1, "greenhouse", "Nebius AI"), Entry(2, "greenhouse", "Nebius") };

        CompanyList.Find(entries, "Nebius").Should().ContainSingle().Which.Id.Should().Be(2);
    }

    [Test]
    public void Find_should_return_every_candidate_of_an_ambiguous_query_shortest_first()
    {
        var entries = new[]
        {
            Entry(1, "greenhouse", "Yandex Cloud"),
            Entry(2, "lever", "Yandex"),
            Entry(3, "greenhouse", "Ozon")
        };

        // «yandex» would hit the exact match and end there - a partial name is what leaves the choice open.
        CompanyList.Find(entries, "yand").Select(e => e.CompanyName).Should().Equal("Yandex", "Yandex Cloud");
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("nothing like this")]
    public void Find_should_return_nothing_for_a_query_that_matches_no_company(string query) =>
        CompanyList.Find([Entry(1, "greenhouse", "Nebius")], query).Should().BeEmpty();

    private static WatchlistEntry Entry(
        long id,
        string sourceId,
        string companyName,
        bool enabled = true,
        BoardOrigin origin = BoardOrigin.Manual) => new()
    {
        Id = id,
        WatchlistId = 1,
        VacancySourceId = sourceId,
        BoardId = companyName.ToLowerInvariant(),
        CompanyName = companyName,
        Enabled = enabled,
        Origin = origin
    };
}