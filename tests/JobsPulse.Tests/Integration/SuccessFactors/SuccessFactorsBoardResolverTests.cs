using FluentAssertions;
using JobsPulse.Sources.SuccessFactors.Models;
using NUnit.Framework;

namespace JobsPulse.Tests.Integration.SuccessFactors;

/// <summary>
/// Live tests of the step that makes discovery work at all: a tenant on a data center host, which is the only thing a
/// crawl index can find, turning into the branded domain that actually publishes the jobs.
/// </summary>
public sealed class SuccessFactorsBoardResolverTests
{
    private static TimeSpan RequestTimeout => TimeSpan.FromMinutes(2);

    [TestCase("https://careers.swissre.com/search/?q=", "careers.swissre.com")]
    [TestCase("https://jobs.corbion.com/job/Gorinchem-Engineer/1234567890/", "jobs.corbion.com")]
    [TestCase("jobs.corbion.com", "jobs.corbion.com")]
    // A site the platform hosts itself: the domain is SAP's, but it is a career site and not a data center host.
    [TestCase("https://ascendlearning.jobs.hr.cloud.sap/job/Leawood-Senior-Software-Engineer-KS-66211/1415909200/",
        "ascendlearning.jobs.hr.cloud.sap")]
    public async Task ResolveByUrl_should_answer_with_the_branded_board(string url, string expected)
    {
        using var host = new SuccessFactorsTestHost();
        using var cts = new CancellationTokenSource(RequestTimeout);

        var candidate = await host.Resolver.ResolveByUrlAsync(url, cts.Token);

        candidate.Should().NotBeNull();
        candidate!.BoardId.Should().Be(expected);
        candidate.JobCount.Should().BeGreaterThan(0);
        candidate.Configuration.Should().NotBeNullOrWhiteSpace();

        SuccessFactorsBoardConfig.FromJson(candidate.Configuration)!.Domain.Should().Be(expected);
    }

    /// <summary>
    /// The bridge: the legacy url names a tenant and nothing else, and it has to come back as the same board the
    /// branded url resolves to - otherwise one company is watched as two boards.
    /// </summary>
    [TestCase("https://career2.successfactors.eu/career?company=corbion", "career2.successfactors.eu",
        "jobs.corbion.com")]
    [TestCase("https://career2.successfactors.eu/career?company=SwissRe", "career2.successfactors.eu",
        "careers.swissre.com")]
    // The deep link to one posting names its tenant the same way, and has to reach the same board.
    [TestCase("https://career5.successfactors.eu/sfcareer/jobreqcareer?jobId=32385&company=kmd&utm_source=chatgpt.com",
        "career5.successfactors.eu", "jobs.kmd.net")]
    public async Task ResolveByUrl_should_translate_a_tenant_into_its_branded_board(
        string url,
        string rcmHost,
        string expected)
    {
        using var host = new SuccessFactorsTestHost();
        using var cts = new CancellationTokenSource(RequestTimeout);

        var candidate = await host.Resolver.ResolveByUrlAsync(url, cts.Token);

        candidate.Should().NotBeNull();
        candidate!.BoardId.Should().Be(expected);

        var config = SuccessFactorsBoardConfig.FromJson(candidate.Configuration)!;

        config.Domain.Should().Be(expected);
        config.RcmHost.Should().Be(rcmHost);
        config.Tenant.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// What the crawl sweep does with a mined token. The candidate must carry the corrected board id, because
    /// `BoardTokenSink` stores the candidate and not the token it asked about.
    /// </summary>
    [Test]
    public async Task Probe_should_correct_a_crawled_token_into_the_branded_board()
    {
        using var host = new SuccessFactorsTestHost();
        using var cts = new CancellationTokenSource(RequestTimeout);

        var candidate = await host.Resolver.ProbeAsync("career2.successfactors.eu/corbion", cts.Token);

        candidate.Should().NotBeNull();
        candidate!.BoardId.Should().Be("jobs.corbion.com");
        candidate.DisplayName.Should().NotBeNullOrWhiteSpace();
    }

    [TestCase("career2.successfactors.eu/there-is-no-such-tenant-here")]
    [TestCase("this-domain-does-not-exist-jobspulse.example")]
    public async Task Probe_should_answer_with_nothing_for_what_is_not_a_board(string boardId)
    {
        using var host = new SuccessFactorsTestHost();
        using var cts = new CancellationTokenSource(RequestTimeout);

        (await host.Resolver.ProbeAsync(boardId, cts.Token)).Should().BeNull();
    }

    /// <summary>A company name predicts neither the domain nor the tenant, so this source never answers by name.</summary>
    [Test]
    public async Task ResolveByName_should_answer_with_nothing()
    {
        using var host = new SuccessFactorsTestHost();

        (await host.Resolver.ResolveByNameAsync("SAP", CancellationToken.None)).Should().BeEmpty();
    }
}
