using FluentAssertions;
using JobsPulse.Core.Model.Domain;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Core.Pipeline;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Tests.Integration.Core;

/// <summary>
/// `PostedWithinDays` against the publication date, and what leaving a watchlist means: a vacancy that aged out of
/// the date window or lost its description to a failed request is still open, so it must not be reported as closed.
/// </summary>
public sealed class ChangeDetectorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    private static readonly FilterSpec Filter = new()
    {
        TitleAnyOf = ["engineer"],
        DescriptionAnyOf = [".net"],
        PostedWithinDays = 120
    };

    private readonly VacancyMatcher matcher = new(new FakeTimeProvider(Now), new SilentLog());

    [Test]
    public void Matcher_should_judge_age_by_publication_date_not_by_first_sight()
    {
        var old = Vacancy("1", published: Now.AddYears(-10), firstSeen: Now);

        matcher.Matches(old, Filter).Should().BeFalse();
        matcher.MayMatchListed(old, Filter).Should().BeFalse();
    }

    [Test]
    public void Matcher_should_fall_back_to_first_sight_without_publication_date()
    {
        var undated = Vacancy("1", published: null, firstSeen: Now.AddDays(-1));

        matcher.Matches(undated, Filter).Should().BeTrue();
    }

    [Test]
    public void Matcher_should_skip_description_rules_when_description_is_unavailable()
    {
        var blind = Vacancy("1", description: null) with { DescriptionUnavailable = true };

        matcher.Matches(blind, Filter).Should().BeTrue();
        matcher.Matches(blind with { DescriptionUnavailable = false }, Filter).Should().BeFalse();
    }

    [Test]
    public void Detect_should_not_report_new_vacancies_published_long_ago()
    {
        var output = Detect([Vacancy("1", published: Now.AddYears(-10))], seen: [], matches: []);

        output.VacanciesChanges.Should().BeEmpty();
        output.VacanciesUpserts.Should().BeEmpty();
    }

    [Test]
    public void Detect_should_drop_an_aged_out_match_silently()
    {
        var aged = Vacancy("1", published: Now.AddDays(-121));

        var output = Detect([aged], seen: [aged], matches: [Match(aged)]);

        output.MatchRemovals.Should().ContainSingle();
        output.VacanciesChanges.Should().BeEmpty();
    }

    [Test]
    public void Detect_should_report_a_vacancy_gone_from_the_board_as_closed()
    {
        var gone = Vacancy("1");

        var output = Detect([], seen: [gone], matches: [Match(gone)]);

        output.VacanciesChanges.Should().ContainSingle(c => c.Kind == VacancyChangeKind.Closed);
    }

    [Test]
    public void Detect_should_keep_a_match_whose_description_could_not_be_read()
    {
        var stored = Vacancy("1");
        var blind = stored with { Description = null, DescriptionUnavailable = true };

        var output = Detect([blind], seen: [stored], matches: [Match(stored)]);

        output.VacanciesChanges.Should().BeEmpty();
        output.MatchRemovals.Should().BeEmpty();
        output.VacanciesUpserts.Should().ContainSingle();
    }

    [Test]
    public void Detect_should_not_add_a_match_whose_description_could_not_be_read()
    {
        var blind = Vacancy("1", description: null) with { DescriptionUnavailable = true };

        var output = Detect([blind], seen: [blind], matches: []);

        output.VacanciesChanges.Should().BeEmpty();
        output.MatchUpserts.Should().BeEmpty();
    }

    private ChangeDetector.Output Detect(
        IReadOnlyList<Vacancy> fetched,
        IReadOnlyList<Vacancy> seen,
        IReadOnlyList<WatchlistMatch> matches)
    {
        return new ChangeDetector(matcher).Detect(new ChangeDetector.Input
        {
            SourceId = "test",
            BoardId = "board",
            Traverse = SourceTraverseResult.Complete(fetched),
            StorageFilters = [Filter],
            Subscriptions =
            [
                new WatchlistSubscription
                {
                    WatchlistId = 1,
                    WatchlistName = "default",
                    CompanyName = "Acme",
                    Filter = Filter,
                    FilterHash = "filter"
                }
            ],
            Seen = seen.ToDictionary(v => v.PostId, v => v with { ContentHash = VacancyHasher.Compute(v) }),
            Matches = matches
        });
    }

    private static WatchlistMatch Match(Vacancy vacancy) => new()
    {
        WatchlistId = 1,
        SourceId = "test",
        BoardId = "board",
        PostId = vacancy.PostId,
        ContentHash = VacancyHasher.Compute(vacancy),
        FilterHash = "filter"
    };

    private static Vacancy Vacancy(
        string postId,
        DateTimeOffset? published = null,
        DateTimeOffset? firstSeen = null,
        string? description = "C# and .NET") => new()
    {
        SourceId = "test",
        BoardId = "board",
        PostId = postId,
        Title = "Backend Engineer",
        Url = $"https://example.com/{postId}",
        FirstPublishedAt = published ?? Now.AddDays(-5),
        FirstSeenAt = firstSeen ?? Now,
        Description = description
    };
}
