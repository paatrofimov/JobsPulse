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
/// A sweep walks the registry slice after slice. The registry, the poll state and the watchlists are the bulk of what
/// a run reads from the database, so they must be read once per sweep, not once per slice.
/// </summary>
public sealed class RegistryPollingServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

    private readonly IBoardRegistryStorage registry = A.Fake<IBoardRegistryStorage>();
    private readonly IWatchlistStorage watchlists = A.Fake<IWatchlistStorage>();
    private readonly IBoardPollStateStorage pollState = A.Fake<IBoardPollStateStorage>();

    // Rereading the poll state per slice loops forever here (the fake never records stamps) - fail instead of hanging.
    [Test, CancelAfter(10_000)]
    public async Task Sweep_should_read_registry_poll_state_and_watchlists_once_for_every_slice(CancellationToken ct)
    {
        A.CallTo(() => registry.ListAsync(A<string?>._, A<int>._, A<CancellationToken>._))
            .Returns(Enumerable.Range(1, 5).Select(Board).ToList());
        A.CallTo(() => pollState.LoadAsync(A<CancellationToken>._))
            .Returns(new Dictionary<string, DateTimeOffset>());
        A.CallTo(() => watchlists.GetEnabledAsync(A<CancellationToken>._))
            .Returns([Watchlist()]);

        var result = await Service().TryRunSweepAsync(Now.AddHours(1), ct);

        result.Started.Should().BeTrue();
        result.Report.BoardsProcessed.Should().Be(5);

        // Five boards in slices of two: three slices, one read of each table.
        A.CallTo(() => pollState.StampAsync(A<IReadOnlyList<BoardPollStamp>>._, A<CancellationToken>._))
            .MustHaveHappened(3, Times.Exactly);
        A.CallTo(() => registry.ListAsync(A<string?>._, A<int>._, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => pollState.LoadAsync(A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => watchlists.GetEnabledAsync(A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    private RegistryPollingService Service()
    {
        var clock = new FakeTimeProvider(Now);
        var log = new SilentLog();
        var matcher = new VacancyMatcher(clock, log);
        var stateStore = A.Fake<IStateStore>();

        // No source is registered, so every board fails fast - the test is about what is read, not what is fetched.
        var catalog = A.Fake<ISourceCatalog>();
        A.CallTo(() => catalog.GetSource(A<string>._)).Returns(null);

        var processor = new BoardProcessor(
            catalog, stateStore, A.Fake<IRejectedPostingStorage>(), new ChangeDetector(matcher), matcher, log);
        var promoter = new DiscoveredBoardPromoter(watchlists, stateStore, matcher, A.Fake<IPollingTrigger>(), log);

        var options = A.Fake<IOptionsMonitor<RegistryPollingOptions>>();
        A.CallTo(() => options.CurrentValue).Returns(new RegistryPollingOptions
        {
            BoardsPerCycle = 2,
            MaxConcurrentBoards = 1,
            DelayBetweenBoardsMs = 0,
            AutoAdd = false
        });

        return new RegistryPollingService(
            registry, watchlists, processor, promoter, pollState, A.Fake<ITraversalProgressTracker>(), options, clock, log);
    }

    private static RegisteredBoard Board(int i) => new()
    {
        SourceId = "greenhouse",
        BoardId = $"board{i}",
        DiscoveredVia = "test"
    };

    private static Watchlist Watchlist() => new()
    {
        Id = 1,
        Name = "default",
        Filter = new FilterSpec { TitleAnyOf = ["engineer"] },
        Entries =
        [
            new WatchlistEntry { WatchlistId = 1, VacancySourceId = "lever", BoardId = "acme", CompanyName = "Acme" }
        ]
    };
}
