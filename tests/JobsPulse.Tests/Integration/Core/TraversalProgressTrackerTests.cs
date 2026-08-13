using FluentAssertions;
using JobsPulse.Core.Infrastructure;
using JobsPulse.Core.Model.Infrastructure;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;

namespace JobsPulse.Tests.Integration.Core;

/// <summary>
/// The counters the admin screen reads. They are written from every concurrent board task of a cycle, so «is the
/// arithmetic right under parallelism» is the point of these tests, not the formatting.
/// </summary>
public sealed class TraversalProgressTrackerTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 13, 10, 0, 0, TimeSpan.Zero);

    [Test]
    public void Snapshot_should_report_every_kind_before_anything_runs()
    {
        var snapshot = NewTracker().Snapshot();

        snapshot.Select(s => s.Kind).Should().BeEquivalentTo(Enum.GetValues<TraversalKind>());
        snapshot.Should().AllSatisfy(s =>
        {
            s.HasRun.Should().BeFalse();
            s.IsRunning.Should().BeFalse();

            // Nothing planned reads as complete, not as a division by zero.
            s.CyclePercent.Should().Be(100);
            s.DatasetPercent.Should().Be(100);
        });
    }

    [Test]
    public void CycleStarted_should_publish_the_plan_and_mark_the_cycle_running()
    {
        var tracker = NewTracker();

        tracker.CycleStarted(TraversalKind.Watchlist, [Units("greenhouse", planned: 3, total: 10, covered: 4)]);

        var progress = Snapshot(tracker, TraversalKind.Watchlist);

        progress.IsRunning.Should().BeTrue();
        progress.StartedAt.Should().Be(Start);
        progress.FinishedAt.Should().BeNull();
        progress.Planned.Should().Be(3);
        progress.Done.Should().Be(0);
        progress.DatasetCovered.Should().Be(4);
        progress.DatasetTotal.Should().Be(10);
        progress.DatasetPercent.Should().Be(40);
    }

    [Test]
    public void UnitFinished_should_count_per_source_and_separate_failures()
    {
        var tracker = NewTracker();

        tracker.CycleStarted(
            TraversalKind.Watchlist,
            [Units("greenhouse", planned: 2, total: 2), Units("lever", planned: 1, total: 1)]);

        tracker.UnitFinished(TraversalKind.Watchlist, "greenhouse", failed: false);
        tracker.UnitFinished(TraversalKind.Watchlist, "greenhouse", failed: true);
        tracker.UnitFinished(TraversalKind.Watchlist, "lever", failed: false);

        var progress = Snapshot(tracker, TraversalKind.Watchlist);

        progress.Done.Should().Be(3);
        progress.Failed.Should().Be(1);
        progress.CyclePercent.Should().Be(100);

        var greenhouse = progress.Sources.Single(s => s.SourceId == "greenhouse");
        greenhouse.Done.Should().Be(2);
        greenhouse.Failed.Should().Be(1);

        // The other cycle walks another dataset and must not see any of this.
        Snapshot(tracker, TraversalKind.Registry).HasRun.Should().BeFalse();
    }

    [Test]
    public void UnitFinished_should_count_a_source_the_plan_never_mentioned()
    {
        var tracker = NewTracker();

        tracker.CycleStarted(TraversalKind.Registry, [Units("greenhouse", planned: 1, total: 1)]);
        tracker.UnitFinished(TraversalKind.Registry, "workday", failed: false);

        Snapshot(tracker, TraversalKind.Registry)
            .Sources.Single(s => s.SourceId == "workday").Done.Should().Be(1);
    }

    [Test]
    public void CycleFinished_should_close_the_cycle_and_refresh_the_coverage()
    {
        var (tracker, clock) = NewTrackerWithClock();

        tracker.CycleStarted(TraversalKind.Watchlist, [Units("greenhouse", planned: 5, total: 10, covered: 0)]);
        tracker.UnitFinished(TraversalKind.Watchlist, "greenhouse", failed: false);

        clock.Advance(TimeSpan.FromMinutes(2));
        tracker.CycleFinished(TraversalKind.Watchlist, [Units("greenhouse", planned: 0, total: 10, covered: 5)]);

        var progress = Snapshot(tracker, TraversalKind.Watchlist);

        progress.IsRunning.Should().BeFalse();
        progress.FinishedAt.Should().Be(Start.AddMinutes(2));

        // Coverage is refreshed, the cycle counters are kept - the finished cycle is what the screen shows next.
        progress.DatasetCovered.Should().Be(5);
        progress.DatasetPercent.Should().Be(50);
        progress.Planned.Should().Be(5);
        progress.Done.Should().Be(1);
    }

    [Test]
    public void CycleFinished_should_be_usable_without_a_started_cycle()
    {
        var tracker = NewTracker();

        // An empty registry closes the cycle it never opened - the screen must not read «still running».
        tracker.CycleFinished(TraversalKind.Registry, []);

        var progress = Snapshot(tracker, TraversalKind.Registry);
        progress.HasRun.Should().BeTrue();
        progress.IsRunning.Should().BeFalse();
    }

    [Test]
    public void UnitFinished_should_be_safe_from_the_concurrent_board_tasks_of_one_cycle()
    {
        var tracker = NewTracker();
        const int boards = 500;

        tracker.CycleStarted(TraversalKind.Registry, [Units("greenhouse", planned: boards, total: boards)]);

        Parallel.For(0, boards, i => tracker.UnitFinished(TraversalKind.Registry, "greenhouse", failed: i % 5 == 0));

        var progress = Snapshot(tracker, TraversalKind.Registry);
        progress.Done.Should().Be(boards);
        progress.Failed.Should().Be(boards / 5);
    }

    private static TraversalProgress Snapshot(TraversalProgressTracker tracker, TraversalKind kind) =>
        tracker.Snapshot().Single(s => s.Kind == kind);

    private static TraversalSourceUnits Units(string sourceId, int planned, int total, int covered = 0) => new()
    {
        SourceId = sourceId,
        Planned = planned,
        DatasetTotal = total,
        DatasetCovered = covered
    };

    private static TraversalProgressTracker NewTracker() => NewTrackerWithClock().Tracker;

    private static (TraversalProgressTracker Tracker, FakeTimeProvider Clock) NewTrackerWithClock()
    {
        var clock = new FakeTimeProvider(Start);

        return (new TraversalProgressTracker(clock), clock);
    }
}
