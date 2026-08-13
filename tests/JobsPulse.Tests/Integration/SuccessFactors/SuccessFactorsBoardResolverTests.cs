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
    [TestCase("https://career2.successfactors.eu/career?company=SwissRe", "careers.swissre.com")]
    [TestCase("https://career2.successfactors.eu/career?company=corbion", "jobs.corbion.com")]
    public async Task ResolveByUrl_should_translate_a_tenant_into_its_branded_board(string url, string expected)
    {
        using var host = new SuccessFactorsTestHost();
        using var cts = new CancellationTokenSource(RequestTimeout);

        var candidate = await host.Resolver.ResolveByUrlAsync(url, cts.Token);

        candidate.Should().NotBeNull();
        candidate!.BoardId.Should().Be(expected);

        var config = SuccessFactorsBoardConfig.FromJson(candidate.Configuration)!;

        config.Domain.Should().Be(expected);
        config.RcmHost.Should().Be("career2.successfactors.eu");
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
