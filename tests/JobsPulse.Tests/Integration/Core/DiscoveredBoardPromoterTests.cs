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
/// A registry board reuses its known postings without reading their descriptions. Such a posting passes the
/// description rules unchecked, so it must not promote a board into a watchlist that has them.
/// </summary>
public sealed class DiscoveredBoardPromoterTests
{
    private static readonly RegisteredBoard Board = new()
    {
        SourceId = "workday",
        BoardId = "acme",
        DiscoveredVia = "test"
    };

    private static readonly Watchlist DotNet = new()
    {
        Id = 1,
        Name = ".NET",
        Filter = new FilterSpec
        {
            TitleAnyOf = ["engineer"],
            DescriptionAnyOf = [".net"]
        }
    };

    private IWatchlistStorage watchlists = null!;
    private IStateStore stateStore = null!;

    [SetUp]
    public void SetUp()
    {
        watchlists = A.Fake<IWatchlistStorage>();
        stateStore = A.Fake<IStateStore>();

        A.CallTo(() => watchlists.AddDiscoveredEntryAsync(
                A<long>._, A<string>._, A<string>._, A<string>._, A<string?>._, A<CancellationToken>._))
            .ReturnsLazily((long id, string source, string board, string company, string? _, CancellationToken _) =>
                new WatchlistEntry { WatchlistId = id, VacancySourceId = source, BoardId = board, CompanyName = company });
        A.CallTo(() => stateStore.CommitAsync(A<StateCommit>._, A<CancellationToken>._))
            .Returns(StateCommitResult.Empty);
    }

    [Test]
    public async Task TryPromoteAsync_should_not_promote_by_a_posting_without_its_description(CancellationToken ct)
    {
        var promotions = await Promoter().TryPromoteAsync(Board, [Posting("1", unavailable: true)], [DotNet], ct);

        promotions.Should().BeEmpty();
        A.CallTo(() => watchlists.AddDiscoveredEntryAsync(
                A<long>._, A<string>._, A<string>._, A<string>._, A<string?>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Test]
    public async Task TryPromoteAsync_should_report_only_postings_whose_description_was_read(CancellationToken ct)
    {
        var promotions = await Promoter().TryPromoteAsync(
            Board, [Posting("1", unavailable: true), Posting("2", unavailable: false)], [DotNet], ct);

        promotions.Should().ContainSingle().Which.Vacancies.Should().Be(1);
        A.CallTo(() => stateStore.CommitAsync(
                A<StateCommit>.That.Matches(c => c.MatchUpserts.Count == 1 && c.MatchUpserts[0].PostId == "2"),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    private DiscoveredBoardPromoter Promoter()
    {
        var log = new SilentLog();

        return new DiscoveredBoardPromoter(
            watchlists, stateStore, new VacancyMatcher(new FakeTimeProvider(), log), A.Fake<IPollingTrigger>(), log);
    }

    private static Vacancy Posting(string postId, bool unavailable) => new()
    {
        SourceId = Board.SourceId,
        BoardId = Board.BoardId,
        PostId = postId,
        Title = "Process Engineer",
        Url = $"https://example.com/{postId}",
        Description = unavailable ? null : "We build our services on .NET 8.",
        DescriptionUnavailable = unavailable
    };
}
