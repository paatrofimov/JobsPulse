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

/// <summary>
/// The scheduled digest: every enabled watchlist with companies, since its previous delivered digest - the first one for
/// the configured period ending now.
/// </summary>
public sealed class DigestServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    // NUnit reuses one fixture instance for every test, so the fakes are recreated per test.
    private IWatchlistStorage watchlists = null!;
    private InMemoryDigestStorage digests = null!;
    private IWatchlistEventStorage events = null!;
    private IStateStore stateStore = null!;
    private IReportSink sink = null!;
    private FakeTimeProvider clock = null!;
    private DigestOptions digestOptions = null!;

    [SetUp]
    public void SetUp()
    {
        watchlists = A.Fake<IWatchlistStorage>();
        digests = new InMemoryDigestStorage();
        events = A.Fake<IWatchlistEventStorage>();
        stateStore = A.Fake<IStateStore>();
        sink = A.Fake<IReportSink>();
        clock = new FakeTimeProvider(Now);
        digestOptions = new DigestOptions { PeriodDays = 3 };

        // A vacancy opened a minute before every run - an empty digest is not sent.
        A.CallTo(() => events.LoadAsync(A<long>._, A<DateTimeOffset>._, A<CancellationToken>._))
            .ReturnsLazily((long id, DateTimeOffset _, CancellationToken _) =>
                (IReadOnlyList<WatchlistEvent>)[Opened(id, clock.GetUtcNow().AddMinutes(-1))]);
        A.CallTo(() => stateStore.CountBoardActivityAsync(A<DateTimeOffset>._, A<CancellationToken>._))
            .Returns(new Dictionary<string, BoardActivity>());
        A.CallTo(() => sink.DeliverDigestAsync(A<Watchlist>._, A<WatchlistStats>._, A<WatchlistDigest>._, A<CancellationToken>._))
            .Returns(DeliveryResult.Ok);
    }

    [Test]
    public async Task SendAsync_should_send_every_enabled_watchlist_with_companies_on_every_call()
    {
        Watchlists(Watchlist(1), Watchlist(2), Watchlist(3, withEntry: false));

        var first = await Service().SendAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromHours(1));
        var second = await Service().SendAsync(CancellationToken.None);

        first.Should().Be(2);
        second.Should().Be(2);
        A.CallTo(() => sink.DeliverDigestAsync(A<Watchlist>.That.Matches(w => w.Id == 3), A<WatchlistStats>._, A<WatchlistDigest>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Test]
    public async Task SendAsync_should_cover_the_configured_period_ending_now()
    {
        digestOptions.PeriodDays = 5;
        Watchlists(Watchlist(1));

        WatchlistStats? sent = null;
        A.CallTo(() => sink.DeliverDigestAsync(A<Watchlist>._, A<WatchlistStats>._, A<WatchlistDigest>._, A<CancellationToken>._))
            .Invokes((Watchlist _, WatchlistStats stats, WatchlistDigest _, CancellationToken _) => sent = stats)
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

        A.CallTo(() => sink.DeliverDigestAsync(A<Watchlist>.That.Matches(w => w.Id == 1), A<WatchlistStats>._, A<WatchlistDigest>._, A<CancellationToken>._))
            .Returns(DeliveryResult.Fail("chat not found"));

        var delivered = await Service().SendAsync(CancellationToken.None);

        delivered.Should().Be(1);
        A.CallTo(() => sink.DeliverDigestAsync(A<Watchlist>.That.Matches(w => w.Id == 2), A<WatchlistStats>._, A<WatchlistDigest>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task SendAsync_should_continue_from_the_previous_delivered_digest()
    {
        Watchlists(Watchlist(1));

        var sent = new List<(WatchlistStats Stats, WatchlistDigest Digest)>();
        A.CallTo(() => sink.DeliverDigestAsync(A<Watchlist>._, A<WatchlistStats>._, A<WatchlistDigest>._, A<CancellationToken>._))
            .Invokes((Watchlist _, WatchlistStats stats, WatchlistDigest digest, CancellationToken _) => sent.Add((stats, digest)))
            .Returns(DeliveryResult.Ok);

        await Service().SendAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromHours(8));
        await Service().SendAsync(CancellationToken.None);

        sent.Should().HaveCount(2);
        sent[1].Stats.From.Should().Be(Now);
        sent[1].Stats.To.Should().Be(Now.AddHours(8));
        sent[1].Digest.PreviousId.Should().Be(sent[0].Digest.Id);
    }

    [Test]
    public async Task SendAsync_should_not_move_the_start_after_a_failed_delivery()
    {
        Watchlists(Watchlist(1));

        A.CallTo(() => sink.DeliverDigestAsync(A<Watchlist>._, A<WatchlistStats>._, A<WatchlistDigest>._, A<CancellationToken>._))
            .Returns(DeliveryResult.Ok).Once()
            .Then.Returns(DeliveryResult.Fail("chat not found")).Once()
            .Then.Returns(DeliveryResult.Ok);

        await Service().SendAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromHours(8));
        await Service().SendAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromHours(8));
        await Service().SendAsync(CancellationToken.None);

        digests.Rows.Should().HaveCount(3);
        digests.Rows[2].From.Should().Be(Now);
        digests.Rows[2].PreviousId.Should().Be(1);
    }

    [Test]
    public async Task SendAsync_should_skip_an_empty_digest_and_cover_its_period_next_time()
    {
        Watchlists(Watchlist(1));

        // Opened before the first digest and 12 hours after it - the 8-hour mark in between has nothing.
        var history = new List<WatchlistEvent> { Opened(1, Now.AddMinutes(-1)) };
        A.CallTo(() => events.LoadAsync(A<long>._, A<DateTimeOffset>._, A<CancellationToken>._))
            .ReturnsLazily(() => (IReadOnlyList<WatchlistEvent>)[.. history]);

        var first = await Service().SendAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromHours(8));
        var empty = await Service().SendAsync(CancellationToken.None);
        history.Add(Opened(1, Now.AddHours(12)));
        clock.Advance(TimeSpan.FromHours(8));
        var third = await Service().SendAsync(CancellationToken.None);

        (first, empty, third).Should().Be((1, 0, 1));
        digests.Rows.Should().HaveCount(2);
        digests.Rows[1].From.Should().Be(Now);
        digests.Rows[1].To.Should().Be(Now.AddHours(16));
    }

    private static WatchlistEvent Opened(long watchlistId, DateTimeOffset at) =>
        new()
        {
            WatchlistId = watchlistId,
            SourceId = "greenhouse",
            BoardId = "acme",
            PostId = $"{at.Ticks}",
            CompanyName = "Acme",
            Kind = VacancyChangeKind.New,
            OccurredAt = at
        };

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
            digests,
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
