using FakeItEasy;
using FluentAssertions;
using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Core.Options;
using JobsPulse.Core.Pipeline;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Tests.Integration.Core;

/// <summary>A run reports its own changes - not those of a job walking at the same time.</summary>
public sealed class RunReportServiceTests
{
    private static readonly DateTimeOffset StartedAt = new(2026, 10, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FinishedAt = StartedAt.AddMinutes(45);

    private IWatchlistStorage watchlists = null!;
    private IWatchlistEventStorage events = null!;
    private IStateStore stateStore = null!;
    private IReportSink sink = null!;
    private FakeTimeProvider clock = null!;
    private DigestOptions digestOptions = null!;

    [SetUp]
    public void SetUp()
    {
        watchlists = A.Fake<IWatchlistStorage>();
        events = A.Fake<IWatchlistEventStorage>();
        stateStore = A.Fake<IStateStore>();
        sink = A.Fake<IReportSink>();
        clock = new FakeTimeProvider(FinishedAt);
        digestOptions = new DigestOptions();

        A.CallTo(() => watchlists.GetEnabledAsync(A<CancellationToken>._))
            .Returns([DigestServiceTests.Watchlist(1), DigestServiceTests.Watchlist(2, withEntry: false)]);
        A.CallTo(() => stateStore.CountBoardActivityAsync(A<DateTimeOffset>._, A<CancellationToken>._))
            .Returns(new Dictionary<string, BoardActivity>());
        A.CallTo(() => sink.DeliverRunAsync(A<Watchlist>._, A<TraversalRunReport>._, A<CancellationToken>._))
            .Returns(DeliveryResult.Ok);
        A.CallTo(() => sink.DeliverDiscoveryAsync(A<DiscoveryRunReport>._, A<CancellationToken>._))
            .Returns(DeliveryResult.Ok);

        A.CallTo(() => events.LoadAsync(1, A<DateTimeOffset>._, A<CancellationToken>._)).Returns(
        [
            Event("1", VacancyChangeKind.New, StartedAt.AddDays(-1), runId: null),
            Event("2", VacancyChangeKind.New, StartedAt.AddMinutes(5), runId: 7),
            Event("3", VacancyChangeKind.New, StartedAt.AddMinutes(6), runId: 8),
            Event("1", VacancyChangeKind.Closed, StartedAt.AddMinutes(10), runId: 7),
            Event("4", VacancyChangeKind.New, StartedAt.AddMinutes(-5), runId: 7)
        ]);
    }

    [Test]
    public async Task SendTraversalAsync_should_count_only_the_events_of_its_run_inside_the_run()
    {
        var cycle = new CycleReport(120, 3000, 400, 300, 3, 2);

        TraversalRunReport? sent = null;
        A.CallTo(() => sink.DeliverRunAsync(A<Watchlist>._, A<TraversalRunReport>._, A<CancellationToken>._))
            .Invokes((Watchlist _, TraversalRunReport report, CancellationToken _) => sent = report)
            .Returns(DeliveryResult.Ok);

        var delivered = await Service().SendTraversalAsync(TraversalKind.Watchlist, 7, StartedAt, cycle, CancellationToken.None);

        delivered.Should().Be(1);
        sent.Should().NotBeNull();
        sent!.Kind.Should().Be(TraversalKind.Watchlist);
        sent.Cycle.Should().Be(cycle);
        sent.Stats.From.Should().Be(StartedAt);
        sent.Stats.To.Should().Be(FinishedAt);
        sent.Stats.Opened.Should().Be(1);
        sent.Stats.Closed.Should().Be(1);
    }

    [Test]
    public async Task SendTraversalAsync_without_a_run_should_count_the_whole_window()
    {
        TraversalRunReport? sent = null;
        A.CallTo(() => sink.DeliverRunAsync(A<Watchlist>._, A<TraversalRunReport>._, A<CancellationToken>._))
            .Invokes((Watchlist _, TraversalRunReport report, CancellationToken _) => sent = report)
            .Returns(DeliveryResult.Ok);

        await Service().SendTraversalAsync(TraversalKind.Registry, null, StartedAt, null, CancellationToken.None);

        sent!.Cycle.Should().BeNull();
        sent.Stats.Opened.Should().Be(2);
        sent.Stats.Closed.Should().Be(1);
    }

    [Test]
    public async Task SendDiscoveryAsync_should_report_the_run()
    {
        var report = new BoardDiscoveryReport(true, 12, 1_000_000, 40, 30, 5);

        var sent = await Service().SendDiscoveryAsync(StartedAt, full: true, report, CancellationToken.None);

        sent.Should().BeTrue();
        A.CallTo(() => sink.DeliverDiscoveryAsync(
                new DiscoveryRunReport(StartedAt, FinishedAt, true, report), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task Reports_should_not_be_sent_when_switched_off()
    {
        digestOptions.RunReports = false;

        await Service().SendTraversalAsync(TraversalKind.Watchlist, 7, StartedAt, null, CancellationToken.None);
        await Service().SendDiscoveryAsync(StartedAt, false, null, CancellationToken.None);

        A.CallTo(sink).MustNotHaveHappened();
    }

    private RunReportService Service() =>
        new(
            watchlists,
            new WatchlistStatsService(events, stateStore, clock),
            sink,
            DigestServiceTests.Monitor(digestOptions),
            DigestServiceTests.Monitor(new DeliveryOptions { DelayBetweenMessagesSeconds = 0 }),
            clock,
            new SilentLog());

    private static WatchlistEvent Event(string post, VacancyChangeKind kind, DateTimeOffset at, long? runId) =>
        new()
        {
            WatchlistId = 1,
            SourceId = "greenhouse",
            BoardId = "acme",
            PostId = post,
            CompanyName = "Acme",
            Kind = kind,
            OccurredAt = at,
            RunId = runId
        };
}
