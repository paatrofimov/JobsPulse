using FluentAssertions;
using JobsPulse.Core.Model.Domain;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Core.Pipeline;
using NUnit.Framework;

namespace JobsPulse.Tests.Integration.Core;

/// <summary>Closures missing from the watchlist history are restored from the closed `seen_vacancy` rows.</summary>
public sealed class WatchlistHistoryRepairTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly Watchlist Watchlist = new()
    {
        Id = 1,
        Name = ".NET",
        Entries = [new WatchlistEntry { VacancySourceId = "greenhouse", BoardId = "acme", CompanyName = "Acme" }]
    };

    [Test]
    public void Plan_should_restore_opening_and_closure_of_a_post_without_history()
    {
        var events = Plan([Closed("1", T0, T0.AddDays(5))], []);

        events.Select(e => (e.PostId, e.Kind, e.OccurredAt, e.CompanyName)).Should().Equal(
            ("1", VacancyChangeKind.New, T0, "Acme"),
            ("1", VacancyChangeKind.Closed, T0.AddDays(5), "Acme"));
    }

    [Test]
    public void Plan_should_close_a_post_whose_history_ends_with_its_opening()
    {
        var events = Plan([Closed("1", T0, T0.AddDays(5))], [Event("1", VacancyChangeKind.New, T0)]);

        events.Should().ContainSingle()
            .Which.Should().Match<WatchlistEvent>(e => e.Kind == VacancyChangeKind.Closed && e.OccurredAt == T0.AddDays(5));
    }

    [Test]
    public void Plan_should_add_nothing_to_a_post_already_removed_or_reopened_later()
    {
        var events = Plan(
            [Closed("1", T0, T0.AddDays(5)), Closed("2", T0, T0.AddDays(5))],
            [
                Event("1", VacancyChangeKind.New, T0),
                Event("1", VacancyChangeKind.Closed, T0.AddDays(5)),
                Event("2", VacancyChangeKind.New, T0.AddDays(6))
            ]);

        events.Should().BeEmpty();
    }

    [Test]
    public void Plan_should_skip_other_boards_and_vacancies_the_filter_rejects()
    {
        var events = Plan(
            [Closed("1", T0, T0.AddDays(5), board: "elsewhere"), Closed("2", T0, T0.AddDays(5), title: "Intern")],
            [],
            v => !v.Title.Contains("Intern"));

        events.Should().BeEmpty();
    }

    [Test]
    public void Plan_should_open_a_current_match_without_history_only()
    {
        var known = Closed("1", T0, T0).Vacancy;
        var unknown = Closed("2", T0.AddDays(1), T0).Vacancy;

        var events = WatchlistHistoryRepair.Plan(
            Watchlist, [], [known, unknown], [Event("1", VacancyChangeKind.New, T0)], _ => true);

        events.Should().ContainSingle().Which.Should().Match<WatchlistEvent>(e =>
            e.PostId == "2" && e.Kind == VacancyChangeKind.New && e.OccurredAt == T0.AddDays(1));
    }

    [Test]
    public void Plan_should_be_idempotent()
    {
        var rows = new[] { Closed("1", T0, T0.AddDays(5)), Closed("2", T0, T0.AddDays(2)) };

        var first = Plan(rows, [Event("2", VacancyChangeKind.New, T0)]);
        var second = Plan(rows, [Event("2", VacancyChangeKind.New, T0), .. first]);

        first.Should().HaveCount(3);
        second.Should().BeEmpty();
    }

    private static IReadOnlyList<WatchlistEvent> Plan(
        IReadOnlyList<SeenVacancySnapshot> rows,
        IReadOnlyList<WatchlistEvent> history,
        Func<Vacancy, bool>? matches = null) =>
        WatchlistHistoryRepair.Plan(Watchlist, rows, [], history, matches ?? (_ => true));

    private static SeenVacancySnapshot Closed(
        string post,
        DateTimeOffset firstSeen,
        DateTimeOffset closed,
        string board = "acme",
        string title = "Engineer") =>
        new()
        {
            Vacancy = new Vacancy
            {
                SourceId = "greenhouse",
                BoardId = board,
                PostId = post,
                Title = title,
                Url = "https://example.com",
                FirstSeenAt = firstSeen
            },
            ClosedAt = closed
        };

    private static WatchlistEvent Event(string post, VacancyChangeKind kind, DateTimeOffset at) =>
        new()
        {
            WatchlistId = 1,
            SourceId = "greenhouse",
            BoardId = "acme",
            PostId = post,
            CompanyName = "Acme",
            Kind = kind,
            OccurredAt = at
        };
}
