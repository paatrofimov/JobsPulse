using FakeItEasy;
using FluentAssertions;
using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Core.Options;
using JobsPulse.Core.Pipeline;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Tests.Integration.Core;

/// <summary>The scheduled digest: every enabled watchlist with companies, for the configured period ending now.</summary>
public sealed class DigestServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    // NUnit reuses one fixture instance for every test, so the fakes are recreated per test.
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
        clock = new FakeTimeProvider(Now);
        digestOptions = new DigestOptions { PeriodDays = 3 };

        A.CallTo(() => events.LoadAsync(A<long>._, A<DateTimeOffset>._, A<CancellationToken>._))
            .Returns(Array.Empty<WatchlistEvent>());
        A.CallTo(() => stateStore.CountBoardActivityAsync(A<DateTimeOffset>._, A<CancellationToken>._))
            .Returns(new Dictionary<string, BoardActivity>());
        A.CallTo(() => sink.DeliverDigestAsync(A<Watchlist>._, A<WatchlistStats>._, A<CancellationToken>._))
            .Returns(DeliveryResult.Ok);
    }

    [Test]
    public async Task SendAsync_should_send_every_enabled_watchlist_with_companies_on_every_call()
    {
        Watchlists(Watchlist(1), Watchlist(2), Watchlist(3, withEntry: false));

        var first = await Service().SendAsync(CancellationToken.None);
        var second = await Service().SendAsync(CancellationToken.None);

        first.Should().Be(2);
        second.Should().Be(2);
        A.CallTo(() => sink.DeliverDigestAsync(A<Watchlist>.That.Matches(w => w.Id == 3), A<WatchlistStats>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Test]
    public async Task SendAsync_should_cover_the_configured_period_ending_now()
    {
        digestOptions.PeriodDays = 5;
        Watchlists(Watchlist(1));

        WatchlistStats? sent = null;
        A.CallTo(() => sink.DeliverDigestAsync(A<Watchlist>._, A<WatchlistStats>._, A<CancellationToken>._))
            .Invokes((Watchlist _, WatchlistStats stats, CancellationToken _) => sent = stats)
            .Returns(DeliveryResult.Ok);

        await Service().SendAsync(CancellationToken.None);

        sent.Should().NotBeNull();
        sent!.Days.Should().Be(5);
        sent.From.Should().Be(Now.AddDays(-5));
        sent.To.Should().Be(Now);
    }

    [Test]
    public async Task SendAsync_should_go_on_after_a_failed_delivery()
    {
        Watchlists(Watchlist(1), Watchlist(2));

        A.CallTo(() => sink.DeliverDigestAsync(A<Watchlist>.That.Matches(w => w.Id == 1), A<WatchlistStats>._, A<CancellationToken>._))
            .Returns(DeliveryResult.Fail("chat not found"));

        var delivered = await Service().SendAsync(CancellationToken.None);

        delivered.Should().Be(1);
        A.CallTo(() => sink.DeliverDigestAsync(A<Watchlist>.That.Matches(w => w.Id == 2), A<WatchlistStats>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    private void Watchlists(params Watchlist[] items) =>
        A.CallTo(() => watchlists.GetEnabledAsync(A<CancellationToken>._)).Returns(items);

    internal static Watchlist Watchlist(long id, bool withEntry = true) =>
        new()
        {
            Id = id,
            Name = $"list-{id}",
            Entries = withEntry
                ? [new WatchlistEntry { VacancySourceId = "greenhouse", BoardId = "acme", CompanyName = "Acme" }]
                : []
        };

    private DigestService Service() =>
        new(
            watchlists,
            new WatchlistStatsService(events, stateStore, clock),
            sink,
            Monitor(digestOptions),
            // No pause between messages - the fake clock would never let it end.
            Monitor(new DeliveryOptions { DelayBetweenMessagesSeconds = 0 }),
            clock,
            new SilentLog());

    internal static IOptionsMonitor<T> Monitor<T>(T value)
    {
        var monitor = A.Fake<IOptionsMonitor<T>>();
        A.CallTo(() => monitor.CurrentValue).Returns(value);

        return monitor;
    }
}
