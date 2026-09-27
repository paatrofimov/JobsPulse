using FakeItEasy;
using FluentAssertions;
using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model.Domain;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Core.Pipeline;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Tests.Integration.Core;

/// <summary>
/// A filter change is applied to every stored row in one run - a row left for a later run would wait there for hours.
/// </summary>
public sealed class FilterMaintenanceServiceTests
{
    private const int BatchLimit = 5000;

    private static readonly FilterSpec Filter = new() { TitleAnyOf = ["engineer"] };

    private readonly IStateStore stateStore = A.Fake<IStateStore>();
    private readonly IWatchlistStorage watchlists = A.Fake<IWatchlistStorage>();

    [SetUp]
    public void SetUp()
    {
        A.CallTo(() => watchlists.GetEnabledAsync(A<CancellationToken>._))
            .Returns([new Watchlist { Id = 1, Name = "default", Filter = Filter }]);

        A.CallTo(() => stateStore.DeleteAsync(A<IReadOnlyList<VacancyKey>>._, A<CancellationToken>._))
            .ReturnsLazily((IReadOnlyList<VacancyKey> keys, CancellationToken _) => keys.Count);

        A.CallTo(() => stateStore.SetFilterHashAsync(
                A<IReadOnlyList<VacancyKey>>._, A<string>._, A<CancellationToken>._))
            .ReturnsLazily((IReadOnlyList<VacancyKey> keys, string _, CancellationToken _) => keys.Count);
    }

    [Test]
    public async Task RunAsync_should_handle_every_stale_row_batch_after_batch()
    {
        A.CallTo(() => stateStore.LoadStaleFilterAsync(
                A<IReadOnlyList<string>>._, A<int>._, A<CancellationToken>._))
            .ReturnsNextFromSequence(Rows(BatchLimit, "Engineer"), Rows(3, "Accountant"));

        var report = await Service().RunAsync(CancellationToken.None);

        report.Should().Be(new FilterMaintenanceReport(BatchLimit + 3, 3, BatchLimit));
        A.CallTo(() => stateStore.LoadStaleFilterAsync(
                A<IReadOnlyList<string>>._, A<int>._, A<CancellationToken>._))
            .MustHaveHappenedTwiceExactly();
    }

    [Test]
    public async Task RunAsync_should_stop_on_a_full_batch_that_changed_nothing()
    {
        var loads = 0;
        var rows = Rows(BatchLimit, "Engineer");

        A.CallTo(() => stateStore.LoadStaleFilterAsync(
                A<IReadOnlyList<string>>._, A<int>._, A<CancellationToken>._))
            .ReturnsLazily(() =>
            {
                loads++;
                return rows;
            });
        A.CallTo(() => stateStore.SetFilterHashAsync(
                A<IReadOnlyList<VacancyKey>>._, A<string>._, A<CancellationToken>._))
            .Returns(0);

        var report = await Service().RunAsync(CancellationToken.None);

        report.Checked.Should().Be(BatchLimit);
        loads.Should().Be(1);
    }

    private FilterMaintenanceService Service() =>
        new(stateStore, watchlists, new VacancyMatcher(new FakeTimeProvider(), new SilentLog()), new SilentLog());

    private static IReadOnlyList<SeenVacancySnapshot> Rows(int count, string title) =>
    [
        .. Enumerable.Range(0, count).Select(i => new SeenVacancySnapshot
        {
            Vacancy = new Vacancy
            {
                SourceId = "test",
                BoardId = "board",
                PostId = $"{title}-{i}",
                Title = title,
                Url = $"https://example.com/{i}"
            }
        })
    ];
}
