using FluentAssertions;
using JobsPulse.Core.Model.Domain.Extensions;
using JobsPulse.Core.Model.Domain;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Core.Pipeline;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace JobsPulse.Tests.Integration.Storage;

/// <summary>
/// The history the statistics are counted from is written by the commit, together with the outbox rows - exactly the
/// notifications that were enqueued, nothing more.
/// </summary>
public sealed class WatchlistEventStorageTests : IntegrationTestBase
{
    private const string Source = "greenhouse";

    private string board = null!;
    private Watchlist watchlist = null!;

    [SetUp]
    public async Task SetUp()
    {
        await using var db = await DbContextFactory.CreateDbContextAsync();
        await db.Database.MigrateAsync();

        board = $"events-{Guid.NewGuid():N}";
        watchlist = (await WatchlistStorage.CreateAsync($"events-{Guid.NewGuid():N}", FilterSpec.MatchAll, null, CancellationToken.None))!;
    }

    [TearDown]
    public async Task TearDown()
    {
        CurrentRun.Id = null;

        await WatchlistStorage.DeleteAsync(watchlist.Id, CancellationToken.None);

        await using var db = await DbContextFactory.CreateDbContextAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM outbox WHERE dedup_key LIKE {Source + "/" + board + "%"}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM seen_vacancy WHERE board_id = {board}");
    }

    [Test]
    public async Task Commit_should_record_openings_and_closures_but_not_updates()
    {
        var a = Vacancy("a");
        var b = Vacancy("b");
        var c = Vacancy("c");

        await CommitAsync([a, b, c], [], [Notify(a, VacancyChangeKind.New), Notify(b, VacancyChangeKind.New), Notify(c, VacancyChangeKind.Updated)]);
        await CommitAsync([], ["a"], [Notify(a, VacancyChangeKind.Closed), Notify(b, VacancyChangeKind.Filtered)]);

        var events = await WatchlistEvents.LoadAsync(watchlist.Id, DateTimeOffset.UtcNow.AddMinutes(1), CancellationToken.None);

        events.Select(e => (e.PostId, e.Kind)).Should().Equal(
            ("a", VacancyChangeKind.New),
            ("b", VacancyChangeKind.New),
            ("a", VacancyChangeKind.Closed),
            ("b", VacancyChangeKind.Filtered));
        events.Should().AllSatisfy(e =>
        {
            e.WatchlistId.Should().Be(watchlist.Id);
            e.SourceId.Should().Be(Source);
            e.BoardId.Should().Be(board);
            e.CompanyName.Should().Be("Acme");
            e.OccurredAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2));
        });
    }

    [Test]
    public async Task Commit_should_not_record_a_notification_the_outbox_deduplicated()
    {
        var a = Vacancy("a");

        var first = await CommitAsync([a], [], [Notify(a, VacancyChangeKind.New)]);
        var second = await CommitAsync([a], [], [Notify(a, VacancyChangeKind.New)]);

        first.OutboxAffectedRows.Should().Be(1);
        second.OutboxAffectedRows.Should().Be(0);

        var events = await WatchlistEvents.LoadAsync(watchlist.Id, DateTimeOffset.UtcNow.AddMinutes(1), CancellationToken.None);
        events.Should().ContainSingle();
    }

    [Test]
    public async Task Commit_should_enqueue_a_notification_of_a_deleted_watchlist_without_history()
    {
        var a = Vacancy("a");
        var missing = watchlist.Id + 1_000_000;

        var result = await CommitAsync([a], [], [Notify(a, VacancyChangeKind.New, missing)]);

        result.OutboxAffectedRows.Should().Be(1);
        (await WatchlistEvents.LoadAsync(missing, DateTimeOffset.UtcNow.AddMinutes(1), CancellationToken.None))
            .Should().BeEmpty();
    }

    [Test]
    public async Task Commit_should_record_where_the_vacancy_is()
    {
        var located = Vacancy("a") with { Location = " Berlin, Germany " };
        var officeOnly = Vacancy("b") with { Offices = ["", "Lisbon"] };
        var nowhere = Vacancy("c");

        await CommitAsync(
            [located, officeOnly, nowhere],
            [],
            [
                Notify(located, VacancyChangeKind.New),
                Notify(officeOnly, VacancyChangeKind.New),
                Notify(nowhere, VacancyChangeKind.New)
            ]);

        var events = await WatchlistEvents.LoadAsync(watchlist.Id, DateTimeOffset.UtcNow.AddMinutes(1), CancellationToken.None);

        events.Select(e => (e.PostId, e.Location)).Should().Equal(
            ("a", "Berlin, Germany"),
            ("b", "Lisbon"),
            ("c", (string?)null));
    }

    [Test]
    public async Task Commit_should_stamp_the_events_with_the_current_run()
    {
        var a = Vacancy("a");
        var b = Vacancy("b");

        CurrentRun.Id = 42;
        await CommitAsync([a], [], [Notify(a, VacancyChangeKind.New)]);

        CurrentRun.Id = null;
        await CommitAsync([b], [], [Notify(b, VacancyChangeKind.New)]);

        var events = await WatchlistEvents.LoadAsync(watchlist.Id, DateTimeOffset.UtcNow.AddMinutes(1), CancellationToken.None);

        events.Select(e => (e.PostId, e.RunId)).Should().Equal(("a", 42L), ("b", (long?)null));
    }

    [Test]
    public async Task AppendAsync_should_store_restored_events()
    {
        var at = DateTimeOffset.UtcNow.AddDays(-2);

        var appended = await WatchlistEvents.AppendAsync(
        [
            new WatchlistEvent
            {
                WatchlistId = watchlist.Id,
                SourceId = Source,
                BoardId = board,
                PostId = "old",
                CompanyName = "Acme",
                Kind = VacancyChangeKind.Closed,
                OccurredAt = at
            }
        ], CancellationToken.None);

        var events = await WatchlistEvents.LoadAsync(watchlist.Id, DateTimeOffset.UtcNow, CancellationToken.None);

        appended.Should().Be(1);
        events.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            PostId = "old",
            Kind = VacancyChangeKind.Closed,
            RunId = (long?)null
        });
        events[0].OccurredAt.Should().BeCloseTo(at, TimeSpan.FromMilliseconds(1));
    }

    [Test]
    public async Task LoadAsync_should_read_only_events_up_to_the_given_moment()
    {
        var a = Vacancy("a");
        await CommitAsync([a], [], [Notify(a, VacancyChangeKind.New)]);

        var events = await WatchlistEvents.LoadAsync(watchlist.Id, DateTimeOffset.UtcNow.AddHours(-1), CancellationToken.None);

        events.Should().BeEmpty();
    }

    [Test]
    public async Task Deleting_a_watchlist_should_delete_its_history()
    {
        var a = Vacancy("a");
        await CommitAsync([a], [], [Notify(a, VacancyChangeKind.New)]);

        await WatchlistStorage.DeleteAsync(watchlist.Id, CancellationToken.None);

        (await WatchlistEvents.LoadAsync(watchlist.Id, DateTimeOffset.UtcNow.AddMinutes(1), CancellationToken.None))
            .Should().BeEmpty();
    }

    [Test]
    public async Task Statistics_should_be_counted_from_the_recorded_history()
    {
        var a = Vacancy("a");
        var b = Vacancy("b");

        await CommitAsync([a, b], [], [Notify(a, VacancyChangeKind.New), Notify(b, VacancyChangeKind.New)]);
        await CommitAsync([], ["a", "b"], [Notify(a, VacancyChangeKind.Closed), Notify(b, VacancyChangeKind.Closed)]);

        var stats = await new WatchlistStatsService(WatchlistEvents, StateStore, TimeProvider.System)
            .ComputeAsync(watchlist, 3, CancellationToken.None);

        stats.Opened.Should().Be(2);
        stats.Closed.Should().Be(2);
        stats.NewCompanies.Should().Equal("Acme");
        stats.EmptiedCompanies.Should().Equal("Acme");
        stats.TopByOpened.Should().Equal(new CompanyCount("Acme", 2));
    }

    private async Task<StateCommitResult> CommitAsync(
        IReadOnlyList<Vacancy> upserts,
        IReadOnlyList<string> closed,
        IReadOnlyList<OutboxItem> notifications) =>
        await CommitAsync(new StateCommit
        {
            SourceId = Source,
            BoardId = board,
            Upserts = upserts,
            ClosedPostIds = closed,
            Notifications = notifications
        });

    private Vacancy Vacancy(string post)
    {
        var vacancy = new Vacancy
        {
            SourceId = Source,
            BoardId = board,
            PostId = post,
            Title = $"Engineer {post}",
            Url = $"https://example.com/{post}"
        };

        return vacancy with { ContentHash = VacancyHasher.Compute(vacancy) };
    }

    private OutboxItem Notify(Vacancy vacancy, VacancyChangeKind kind, long? watchlistId = null)
    {
        var id = watchlistId ?? watchlist.Id;

        return new OutboxItem
        {
            ChangeKind = kind,
            CompanyName = "Acme",
            WatchlistId = id,
            WatchlistName = watchlist.Name,
            Vacancy = vacancy,
            DedupKey = vacancy.ToDedupKey(kind, vacancy.ContentHash, id)
        };
    }
}
