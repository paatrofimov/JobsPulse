using FluentAssertions;
using JobsPulse.Core.Model.Domain;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Sources.SuccessFactors.Infrastructure;
using NUnit.Framework;

namespace JobsPulse.Tests.Integration.SuccessFactors;

/// <summary>
/// Live tests against real career sites, the same way the Greenhouse source is tested. The boards are picked to cover
/// what actually differs between sites rather than to be many: a small one, one whose '/sitemap.xml' is the url list
/// and one whose '/sitemap.xml' is the feed, one that publishes in a locale other than English, and an empty one.
/// </summary>
public sealed class SuccessFactorsBoardSourceTests
{
    private static TimeSpan RequestTimeout => TimeSpan.FromMinutes(3);

    private static SourceTarget Target(string boardId, bool descriptions = false) => new()
    {
        SourceId = SuccessFactorsMapper.SourceId,
        BoardId = boardId,
        IncludeDescriptions = descriptions
    };

    [TestCase("jobs.corbion.com", false)]
    [TestCase("jobs.corbion.com", true)]
    [TestCase("careers.swissre.com", false)]
    [TestCase("jobs.csiro.au", false)]
    public async Task TraverseTarget_should_read_a_whole_board(string boardId, bool descriptions)
    {
        using var host = new SuccessFactorsTestHost();
        using var cts = new CancellationTokenSource(RequestTimeout);

        var result = await host.Source.TraverseTargetAsync(Target(boardId, descriptions), cts.Token);

        TestContext.Progress.WriteLine(
            $"{boardId}: complete={result.IsComplete}, missing={result.BoardMissing}, " +
            $"vacancies={result.Vacancies.Count}, error={result.Error ?? "-"}");

        result.Error.Should().BeNull();
        result.BoardMissing.Should().BeFalse();
        result.IsComplete.Should().BeTrue();
        result.Vacancies.Should().NotBeEmpty();

        foreach (var vacancy in result.Vacancies)
        {
            vacancy.PostId.Should().NotBeNullOrWhiteSpace();
            vacancy.Title.Should().NotBeNullOrWhiteSpace();
            vacancy.Url.Should().StartWith("http");
            vacancy.BoardId.Should().Be(boardId);

            // The location is appended to the feed's title and has to be gone from ours.
            if (!string.IsNullOrEmpty(vacancy.Location))
                vacancy.Title.Should().NotEndWith($"({vacancy.Location})");

            if (descriptions)
                vacancy.Description.Should().NotBeNullOrWhiteSpace();
        }

        result.Vacancies.Select(v => v.PostId).Should().OnlyHaveUniqueItems();
    }

    /// <summary>
    /// The whole point of the feed being one document: a board is one request, and a second poll of an unchanged
    /// board produces the same content hash - the check that mapping no dates costs nothing.
    /// </summary>
    [Test]
    public async Task TraverseTarget_should_be_stable_across_two_polls()
    {
        using var host = new SuccessFactorsTestHost();
        using var cts = new CancellationTokenSource(RequestTimeout);

        var first = await host.Source.TraverseTargetAsync(Target("jobs.corbion.com"), cts.Token);
        var second = await host.Source.TraverseTargetAsync(Target("jobs.corbion.com"), cts.Token);

        static string Fingerprint(Vacancy v) => $"{v.PostId}|{v.Title}|{v.Location}|{v.Url}";

        second.Vacancies.Select(Fingerprint).Should().BeEquivalentTo(first.Vacancies.Select(Fingerprint));
    }

    /// <summary>A domain that answers but publishes nothing is a complete traversal of an empty board, not a failure.</summary>
    [Test]
    public async Task TraverseTarget_should_report_an_empty_board_as_complete()
    {
        using var host = new SuccessFactorsTestHost();
        using var cts = new CancellationTokenSource(RequestTimeout);

        var result = await host.Source.TraverseTargetAsync(Target("careerssf.rcu.gov.sa"), cts.Token);

        TestContext.Progress.WriteLine(
            $"empty board: complete={result.IsComplete}, vacancies={result.Vacancies.Count}, error={result.Error ?? "-"}");

        result.IsComplete.Should().BeTrue();
        result.BoardMissing.Should().BeFalse();
        result.Vacancies.Should().BeEmpty();
    }

    /// <summary>
    /// A domain that is not a career site must never come back as a missing board: `BoardMissing` closes every
    /// vacancy of the board and disables the watchlist entries pointing at it.
    /// </summary>
    [Test]
    public async Task TraverseTarget_should_not_call_an_unreadable_site_a_missing_board()
    {
        using var host = new SuccessFactorsTestHost(("EnableHtmlFallback", "false"));
        using var cts = new CancellationTokenSource(RequestTimeout);

        var result = await host.Source.TraverseTargetAsync(Target("www.iana.org"), cts.Token);

        TestContext.Progress.WriteLine($"non-site: missing={result.BoardMissing}, error={result.Error ?? "-"}");

        result.IsComplete.Should().BeFalse();
        result.BoardMissing.Should().BeFalse();
        result.Error.Should().NotBeNull();
    }

    /// <summary>
    /// A feed over the byte budget must fall back rather than commit a part of the board, and must never be
    /// mistaken for a board that shrank.
    /// </summary>
    [Test]
    public async Task TraverseTarget_should_fall_back_to_the_html_listing_when_the_feed_does_not_fit()
    {
        using var host = new SuccessFactorsTestHost(("MaxFeedBytes", "20000"));
        using var cts = new CancellationTokenSource(RequestTimeout);

        var result = await host.Source.TraverseTargetAsync(Target("jobs.corbion.com"), cts.Token);

        TestContext.Progress.WriteLine(
            $"budgeted: complete={result.IsComplete}, vacancies={result.Vacancies.Count}, error={result.Error ?? "-"}");

        result.BoardMissing.Should().BeFalse();
        result.IsComplete.Should().BeTrue();
        result.Vacancies.Should().NotBeEmpty();
    }

    /// <summary>With no fallback left, a board whose feed does not fit is a failure - never a partial commit.</summary>
    [Test]
    public async Task TraverseTarget_should_refuse_a_truncated_feed_when_there_is_no_fallback()
    {
        using var host = new SuccessFactorsTestHost(("MaxFeedBytes", "20000"), ("EnableHtmlFallback", "false"));
        using var cts = new CancellationTokenSource(RequestTimeout);

        var result = await host.Source.TraverseTargetAsync(Target("jobs.corbion.com"), cts.Token);

        result.IsComplete.Should().BeFalse();
        result.BoardMissing.Should().BeFalse();
        result.Vacancies.Should().BeEmpty();
        result.Error.Should().Contain("budget");
    }
}
