using FluentAssertions;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Core.Pipeline;
using NUnit.Framework;

namespace JobsPulse.Tests.Integration.Core;

/// <summary>
/// The statistics of a period are replayed from the event history of a watchlist - the rules below are what «new
/// company» and «closed everything» mean.
/// </summary>
public sealed class WatchlistStatsCalculatorTests
{
    private static readonly DateTimeOffset To = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset From = To.AddDays(-3);

    private static readonly IReadOnlyDictionary<string, BoardActivity> NoActivity =
        new Dictionary<string, BoardActivity>();

    [Test]
    public void Compute_should_count_opened_and_closed_vacancies_inside_the_period_only()
    {
        var stats = Compute(
        [
            Event("acme", "1", VacancyChangeKind.New, From.AddDays(-1)),
            Event("acme", "2", VacancyChangeKind.New, From.AddHours(1)),
            Event("acme", "3", VacancyChangeKind.New, To),
            Event("acme", "1", VacancyChangeKind.Closed, From.AddHours(2)),
            Event("acme", "2", VacancyChangeKind.AgedOut, From.AddHours(3)),
            Event("acme", "3", VacancyChangeKind.Filtered, To)
        ]);

        stats.Opened.Should().Be(2);
        stats.Closed.Should().Be(1);
        stats.Dropped.Should().Be(2);
        stats.OpenedAndEnded.Should().Be(2);
    }

    [Test]
    public void Compute_should_make_open_at_start_plus_opened_minus_ended_equal_open_at_end()
    {
        var stats = Compute(
        [
            Event("acme", "1", VacancyChangeKind.New, From.AddDays(-1)),
            Event("acme", "2", VacancyChangeKind.New, From.AddDays(-1)),

            // A repeated close moves nothing and is not counted twice.
            Event("acme", "1", VacancyChangeKind.Closed, From.AddHours(1)),
            Event("acme", "1", VacancyChangeKind.Closed, From.AddHours(2)),

            // Reopened inside the period - opened again, and still open at the end.
            Event("acme", "1", VacancyChangeKind.New, From.AddHours(3)),

            // A close of a post never seen open moves nothing either.
            Event("acme", "9", VacancyChangeKind.Closed, From.AddHours(3)),

            Event("beta", "1", VacancyChangeKind.New, From.AddHours(4)),
            Event("beta", "1", VacancyChangeKind.Filtered, From.AddHours(5)),
            Event("beta", "2", VacancyChangeKind.New, From.AddHours(6))
        ]);

        stats.OpenAtStart!.Vacancies.Should().Be(2);
        stats.OpenAtEnd!.Vacancies.Should().Be(3);
        (stats.Opened, stats.Closed, stats.Dropped, stats.OpenedAndEnded).Should().Be((3, 1, 1, 1));
        (stats.OpenAtStart.Vacancies + stats.Opened - stats.Closed - stats.Dropped).Should().Be(stats.OpenAtEnd.Vacancies);

        stats.ByCompany.Should().BeEquivalentTo(
            [
                new CompanyChanges { CompanyName = "acme", Opened = 1, Closed = 1, Dropped = 0 },
                new CompanyChanges { CompanyName = "beta", Opened = 2, Closed = 0, Dropped = 1 }
            ],
            o => o.WithoutStrictOrdering());
        stats.ByCompany.Single(x => x.CompanyName == "beta").Net.Should().Be(1);
    }

    [Test]
    public void Compute_should_treat_a_company_without_open_vacancies_at_the_start_as_new()
    {
        var stats = Compute(
        [
            // Had one before, closed before the period - opening again inside it makes the company new.
            Event("returning", "1", VacancyChangeKind.New, From.AddDays(-10)),
            Event("returning", "1", VacancyChangeKind.Closed, From.AddDays(-5)),
            Event("returning", "2", VacancyChangeKind.New, From.AddHours(5)),

            // Still had one open when the period began - not new.
            Event("steady", "1", VacancyChangeKind.New, From.AddDays(-2)),
            Event("steady", "2", VacancyChangeKind.New, From.AddHours(5)),

            Event("fresh", "1", VacancyChangeKind.New, From.AddHours(1))
        ]);

        stats.NewCompanies.Should().Equal("fresh", "returning");
    }

    [Test]
    public void Compute_should_list_companies_that_closed_every_matching_vacancy()
    {
        var stats = Compute(
        [
            Event("gone", "1", VacancyChangeKind.New, From.AddDays(-5)),
            Event("gone", "2", VacancyChangeKind.New, From.AddDays(-4)),
            Event("gone", "1", VacancyChangeKind.Closed, From.AddHours(1)),
            Event("gone", "2", VacancyChangeKind.Closed, From.AddHours(2)),

            // Closed one, still has another.
            Event("partial", "1", VacancyChangeKind.New, From.AddDays(-5)),
            Event("partial", "2", VacancyChangeKind.New, From.AddDays(-5)),
            Event("partial", "1", VacancyChangeKind.Closed, From.AddHours(1)),

            // Closed one and opened a new one afterwards.
            Event("rehiring", "1", VacancyChangeKind.New, From.AddDays(-5)),
            Event("rehiring", "1", VacancyChangeKind.Closed, From.AddHours(1)),
            Event("rehiring", "2", VacancyChangeKind.New, From.AddHours(2)),

            // Nothing open any more because the filter ruled the vacancy out - emptied all the same.
            Event("filtered", "1", VacancyChangeKind.New, From.AddDays(-5)),
            Event("filtered", "1", VacancyChangeKind.Filtered, From.AddHours(1)),

            // Emptied before the period - old news.
            Event("long-gone", "1", VacancyChangeKind.New, From.AddDays(-9)),
            Event("long-gone", "1", VacancyChangeKind.Closed, From.AddDays(-8))
        ]);

        stats.EmptiedCompanies.Should().Equal("filtered", "gone");
    }

    [Test]
    public void Compute_should_count_a_company_whose_last_closure_mixes_with_an_aged_out_vacancy_as_emptied()
    {
        var stats = Compute(
        [
            Event("acme", "1", VacancyChangeKind.New, From.AddDays(-5)),
            Event("acme", "2", VacancyChangeKind.New, From.AddDays(-5)),
            Event("acme", "1", VacancyChangeKind.AgedOut, From.AddHours(1)),
            Event("acme", "2", VacancyChangeKind.Closed, From.AddHours(2))
        ]);

        stats.EmptiedCompanies.Should().Equal("acme");
    }

    [Test]
    public void Compute_should_rank_companies_by_vacancies_opened_in_the_period()
    {
        var stats = Compute(
        [
            Event("a", "1", VacancyChangeKind.New, From.AddHours(1)),
            Event("b", "1", VacancyChangeKind.New, From.AddHours(1)),
            Event("b", "2", VacancyChangeKind.New, From.AddHours(1)),
            Event("c", "1", VacancyChangeKind.New, From.AddHours(1)),
            Event("c", "2", VacancyChangeKind.New, From.AddHours(1)),
            Event("c", "3", VacancyChangeKind.New, From.AddHours(1)),
            Event("d", "1", VacancyChangeKind.New, From.AddHours(1)),
            Event("e", "1", VacancyChangeKind.New, From.AddDays(-1)),
            Event("e", "2", VacancyChangeKind.New, From.AddDays(-1)),
            Event("e", "3", VacancyChangeKind.New, From.AddDays(-1)),
            Event("e", "4", VacancyChangeKind.New, From.AddDays(-1))
        ]);

        stats.TopByOpened.Should().Equal(
            new CompanyCount("c", 3),
            new CompanyCount("b", 2),
            new CompanyCount("a", 1));
    }

    [Test]
    public void Compute_should_rank_enabled_boards_of_the_watchlist_by_activity()
    {
        var watchlist = Watchlist(
            Entry("busy"),
            Entry("calm"),
            Entry("idle"),
            Entry("hot"),
            Entry("off", enabled: false));

        var activity = new Dictionary<string, BoardActivity>(StringComparer.OrdinalIgnoreCase)
        {
            ["greenhouse/busy"] = new(5, 3, 2, 0.1),
            ["greenhouse/calm"] = new(1, 0, 0, 0.1),
            ["greenhouse/hot"] = new(20, 0, 0, 0.1),
            ["greenhouse/off"] = new(100, 0, 0, 0.1),
            ["greenhouse/elsewhere"] = new(100, 0, 0, 0.1),
            ["greenhouse/idle"] = new(0, 0, 0, 0.1)
        };

        var stats = WatchlistStatsCalculator.Compute(watchlist, [], activity, From, To, 3);

        stats.TopByActivity.Select(x => x.CompanyName).Should().Equal("Hot Inc", "Busy Inc", "Calm Inc");
        stats.TopByActivity[1].Activity.Events.Should().Be(10);
    }

    [Test]
    public void Compute_should_leave_disabled_companies_out_of_every_number()
    {
        var watchlist = Watchlist(Entry("acme"), Entry("off", enabled: false));

        var stats = WatchlistStatsCalculator.Compute(
            watchlist,
            [
                Event("acme", "1", VacancyChangeKind.New, From.AddHours(1)),
                Event("off", "1", VacancyChangeKind.New, From.AddHours(1)),
                Event("off", "2", VacancyChangeKind.New, From.AddHours(1)),
                Event("off", "3", VacancyChangeKind.New, From.AddDays(-1)),
                Event("off", "3", VacancyChangeKind.Closed, From.AddHours(2))
            ],
            NoActivity,
            From,
            To,
            3);

        stats.Opened.Should().Be(1);
        stats.Closed.Should().Be(0);
        stats.NewCompanies.Should().Equal("Acme Inc");
        stats.EmptiedCompanies.Should().BeEmpty();
        stats.TopByOpened.Should().Equal(new CompanyCount("Acme Inc", 1));
    }

    [Test]
    public void Compute_should_name_companies_by_their_entry_and_fall_back_to_the_reported_name()
    {
        var watchlist = Watchlist(Entry("acme"));

        var stats = WatchlistStatsCalculator.Compute(
            watchlist,
            [
                Event("acme", "1", VacancyChangeKind.New, From.AddHours(1), company: "Old Acme name"),
                Event("removed", "1", VacancyChangeKind.New, From.AddHours(1), company: "Removed Ltd")
            ],
            NoActivity,
            From,
            To,
            3);

        stats.NewCompanies.Should().Equal("Acme Inc", "Removed Ltd");
    }

    [Test]
    public void Compute_should_return_empty_statistics_without_history()
    {
        var stats = WatchlistStatsCalculator.Compute(Watchlist(Entry("acme")), [], NoActivity, From, To, 3);

        stats.Should().BeEquivalentTo(new
        {
            WatchlistId = 7L,
            WatchlistName = "backend",
            Days = 3,
            From,
            To,
            Opened = 0,
            Closed = 0,
            NewCompanies = Array.Empty<string>(),
            EmptiedCompanies = Array.Empty<string>(),
            TopByActivity = Array.Empty<CompanyActivity>(),
            TopByOpened = Array.Empty<CompanyCount>()
        });
    }

    [Test]
    public void Compute_should_count_one_company_watched_through_two_boards_once()
    {
        var watchlist = Watchlist(Entry("az-one"), Entry("az-two"));
        watchlist = watchlist with
        {
            Entries = [.. watchlist.Entries.Select(e => e with { CompanyName = "AstraZeneca" })]
        };

        var activity = new Dictionary<string, BoardActivity>(StringComparer.OrdinalIgnoreCase)
        {
            ["greenhouse/az-one"] = new(2, 0, 1, 0.1),
            ["greenhouse/az-two"] = new(1, 1, 0, 0.1)
        };

        var stats = WatchlistStatsCalculator.Compute(
            watchlist,
            [
                Event("az-one", "1", VacancyChangeKind.New, From.AddHours(1)),
                Event("az-two", "1", VacancyChangeKind.New, From.AddHours(2))
            ],
            activity,
            From,
            To,
            3);

        stats.NewCompanies.Should().Equal("AstraZeneca");
        stats.TopByOpened.Should().Equal(new CompanyCount("AstraZeneca", 2));
        stats.TopByActivity.Should().ContainSingle().Which.Activity.Should().Be(new BoardActivity(3, 1, 1, 0.1));
    }

    [Test]
    public void Compute_should_count_only_the_selected_events_but_replay_every_one()
    {
        var stats = WatchlistStatsCalculator.Compute(
            Watchlist(),
            [
                // Opened by the other job inside the period - not counted, but the company is no longer «new».
                Event("acme", "1", VacancyChangeKind.New, From.AddHours(1), runId: 2),
                Event("acme", "2", VacancyChangeKind.New, From.AddHours(2), runId: 1),
                Event("beta", "1", VacancyChangeKind.New, From.AddHours(2), runId: 1),
                Event("beta", "1", VacancyChangeKind.Closed, From.AddHours(3), runId: 2)
            ],
            NoActivity,
            From,
            To,
            0,
            e => e.RunId == 1);

        stats.Opened.Should().Be(2);
        stats.Closed.Should().Be(0);
        stats.NewCompanies.Should().Equal("acme", "beta");
        stats.EmptiedCompanies.Should().BeEmpty();
    }

    [Test]
    public void Compute_should_count_what_was_open_at_both_ends_of_the_period()
    {
        var watchlist = Watchlist(Entry("acme"), Entry("beta"), Entry("off", enabled: false));

        var stats = WatchlistStatsCalculator.Compute(
            watchlist,
            [
                Event("acme", "1", VacancyChangeKind.New, From.AddDays(-2)),
                Event("acme", "2", VacancyChangeKind.New, From.AddDays(-1)),
                Event("off", "1", VacancyChangeKind.New, From.AddDays(-1)),
                Event("acme", "1", VacancyChangeKind.Closed, From.AddHours(1)),
                Event("beta", "1", VacancyChangeKind.New, From.AddHours(2)),
                Event("beta", "2", VacancyChangeKind.New, From.AddHours(3))
            ],
            NoActivity,
            From,
            To,
            3);

        // The disabled company is counted at neither end.
        stats.OpenAtStart.Should().Be(new OpenCounts(2, 1));
        stats.OpenAtEnd.Should().Be(new OpenCounts(3, 2));
    }

    private static WatchlistStats Compute(IReadOnlyList<WatchlistEvent> events) =>
        WatchlistStatsCalculator.Compute(Watchlist(), events, NoActivity, From, To, 3);

    private static Watchlist Watchlist(params WatchlistEntry[] entries) =>
        new() { Id = 7, Name = "backend", Entries = entries };

    private static WatchlistEntry Entry(string board, bool enabled = true) =>
        new()
        {
            VacancySourceId = "greenhouse",
            BoardId = board,
            CompanyName = $"{char.ToUpperInvariant(board[0])}{board[1..]} Inc",
            Enabled = enabled
        };

    private static WatchlistEvent Event(
        string board,
        string post,
        VacancyChangeKind kind,
        DateTimeOffset at,
        string? company = null,
        long? runId = null,
        string? location = null) =>
        new()
        {
            WatchlistId = 7,
            SourceId = "greenhouse",
            BoardId = board,
            PostId = post,
            CompanyName = company ?? board,
            Kind = kind,
            OccurredAt = at,
            RunId = runId,
            Location = location
        };
}
