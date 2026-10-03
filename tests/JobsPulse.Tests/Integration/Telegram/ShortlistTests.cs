using FluentAssertions;
using JobsPulse.Core.Model.Domain;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Sinks.Telegram.Infrastructure;
using JobsPulse.Sinks.Telegram.Models;
using NUnit.Framework;

namespace JobsPulse.Tests.Integration.Telegram;

/// <summary>The shortlist ranks companies by vacancies in the slice and their vacancies by relevance, then freshness.</summary>
public sealed class ShortlistTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public void Build_should_rank_companies_by_vacancies_and_leave_disabled_ones_out()
    {
        var watchlist = Watchlist(Entry(1, "acme"), Entry(2, "beta"), Entry(3, "off", enabled: false));

        var result = Shortlist.Build(
            watchlist,
            [
                Vacancy("acme", "1", "Backend Engineer", 1),
                Vacancy("beta", "1", "Backend Engineer", 1),
                Vacancy("beta", "2", "Backend Engineer", 2),
                Vacancy("off", "1", "Backend Engineer", 1),
                Vacancy("off", "2", "Backend Engineer", 1),
                Vacancy("off", "3", "Backend Engineer", 1),
                Vacancy("gone", "1", "Backend Engineer", 1)
            ],
            _ => true);

        result.Select(c => (c.CompanyName, c.EntryId, c.Count)).Should().Equal(("Beta", 2L, 2), ("Acme", 1L, 1));
    }

    [Test]
    public void Build_should_pick_the_most_relevant_then_freshest_vacancies()
    {
        var watchlist = Watchlist(Entry(1, "acme")) with
        {
            Filter = new FilterSpec { TitleAnyOf = ["backend", "senior", ".net"] }
        };

        var result = Shortlist.Build(
            watchlist,
            [
                Vacancy("acme", "1", "Backend Engineer", 1),
                Vacancy("acme", "2", "Senior Backend Engineer", 20),
                Vacancy("acme", "3", "Senior .NET Backend Engineer", 30),
                Vacancy("acme", "4", "Senior Backend Developer", 2)
            ],
            _ => true);

        result.Should().ContainSingle().Which.Top.Select(v => v.PostId).Should().Equal("3", "4", "2");
        result[0].Count.Should().Be(4);
    }

    [Test]
    public void PublishedWithin_should_keep_only_vacancies_of_the_last_days()
    {
        var watchlist = Watchlist(Entry(1, "acme"));

        var result = Shortlist.Build(
            watchlist,
            [Vacancy("acme", "1", "Backend", 2), Vacancy("acme", "2", "Backend", 8)],
            Shortlist.PublishedWithin(7, Now));

        result.Should().ContainSingle().Which.Top.Select(v => v.PostId).Should().Equal("1");
    }

    [TestCase("Berlin, Germany", FocusArea.WesternEurope, true)]
    [TestCase("Berlin, Germany", FocusArea.EasternEurope, false)]
    [TestCase("Warsaw, Poland", FocusArea.EasternEurope, true)]
    [TestCase("Remote - EMEA", FocusArea.WesternEurope, true)]
    [TestCase("Remote - EMEA", FocusArea.EasternEurope, true)]
    [TestCase("New York, NY, United States", FocusArea.Usa, true)]
    [TestCase("Toronto, Canada", FocusArea.Usa, false)]
    [TestCase("Latin America", FocusArea.Usa, false)]
    [TestCase("Singapore", FocusArea.Asia, true)]
    [TestCase("Remote", FocusArea.WesternEurope, false)]
    public void IsIn_should_place_a_vacancy_into_focus_areas(string location, FocusArea area, bool expected)
    {
        LocationRegions.IsIn(Vacancy("acme", "1", "Backend", 1) with { Location = location }, area)
            .Should().Be(expected);
    }

    [Test]
    public void Splitting_the_areas_should_keep_the_regions_as_they_were()
    {
        LocationRegions.Of("Warsaw, Poland", []).Should().Be(LocationRegion.Europe);
        LocationRegions.Of("London, UK", []).Should().Be(LocationRegion.Europe);
        LocationRegions.Of("Toronto, Canada", []).Should().Be(LocationRegion.Americas);
        LocationRegions.Of("Austin, TX, USA", []).Should().Be(LocationRegion.Americas);
    }

    private static Watchlist Watchlist(params WatchlistEntry[] entries) =>
        new() { Id = 7, Name = "backend", Entries = entries };

    private static WatchlistEntry Entry(long id, string board, bool enabled = true) =>
        new()
        {
            Id = id,
            VacancySourceId = "greenhouse",
            BoardId = board,
            CompanyName = $"{char.ToUpperInvariant(board[0])}{board[1..]}",
            Enabled = enabled
        };

    private static Vacancy Vacancy(string board, string post, string title, int daysAgo) =>
        new()
        {
            SourceId = "greenhouse",
            BoardId = board,
            PostId = post,
            Title = title,
            Url = $"https://example.com/{board}/{post}",
            FirstPublishedAt = Now.AddDays(-daysAgo)
        };
}
