using FluentAssertions;
using JobsPulse.Core.Abstractions;
using JobsPulse.Discovery.Infrastructure;
using JobsPulse.Discovery.Models;
using JobsPulse.Sources.SuccessFactors.Infrastructure;
using NUnit.Framework;

namespace JobsPulse.Tests.Integration.SuccessFactors;

/// <summary>
/// SuccessFactors is the only source whose board id lives in the query string, so the columnar reader had to learn to
/// project a query parameter. These tests pin that down - both that it happens for this source and that it does not
/// happen for anyone else, because the projected columns are shared by one query answering for every ATS at once.
/// </summary>
public sealed class SuccessFactorsDiscoveryTargetTests
{
    private static IReadOnlyList<BoardIndexTarget> Targets() =>
        BoardIndexTargets.From([new SuccessFactorsBoardUrlParser()]);

    [Test]
    public void From_should_read_the_query_key_off_the_pattern()
    {
        var targets = Targets();

        var careerHost = targets.Single(t => t.Host == "successfactors.com" && t.PathPrefix == "/career");

        careerHost.HostIsSuffix.Should().BeTrue();
        careerHost.Tld.Should().Be("com");
        careerHost.QueryKeys.Should().Equal("company");

        // The platform's own hosted sites name no parameter - their domain is already the board.
        targets.Single(t => t.Host == "jobs2web.com").QueryKeys.Should().BeEmpty();
    }

    /// <summary>
    /// The deep link to one posting is on a path of its own ('/sfcareer/jobreqcareer?jobId=..&amp;company=..'), and it
    /// is the form most crawled legacy urls have. Asking only for '/career' finds none of them, and the path predicate
    /// is a prefix - so the pattern has to be there.
    /// </summary>
    [Test]
    public void From_should_cover_the_legacy_deep_link_path()
    {
        var deepLink = Targets().Single(t => t.Host == "successfactors.eu" && t.PathPrefix == "/sfcareer/");

        deepLink.HostIsSuffix.Should().BeTrue();
        deepLink.QueryKeys.Should().Equal("company");
    }

    /// <summary>
    /// A platform-hosted site is a whole domain of SAP's, so it is asked for like one - and without a query key, since
    /// its host is already the board id.
    /// </summary>
    [Test]
    public void From_should_cover_the_platform_hosted_domains()
    {
        var hosted = Targets().Single(t => t.Host == "jobs.hr.cloud.sap");

        hosted.HostIsSuffix.Should().BeTrue();
        hosted.Tld.Should().Be("sap");
        hosted.PathPrefix.Should().Be("/");
        hosted.QueryKeys.Should().BeEmpty();
    }

    [Test]
    public void From_should_cover_every_data_center_domain()
    {
        Targets().Select(t => t.Host).Should().Contain(
            ["successfactors.com", "successfactors.eu", "sapsf.com", "sapsf.eu", "ns2cloud.com", "hr.cloud.sap"]);
    }

    [Test]
    public void BoardUrls_should_project_the_query_parameter_once()
    {
        var sql = ParquetIndexSql.BoardUrls(new ParquetIndexQuery
        {
            Files = ["s3://x/one.parquet"],
            Targets = Targets()
        });

        // One extra column, extracting the parameter rather than selecting the whole query - a board's job urls
        // differ by their other parameters and would come back one row per posting.
        sql.Should().Contain("regexp_extract(coalesce(url_query, ''), '(?:^|&)company=([^&]*)', 1) AS url_query_company");

        // Exactly two mentions: the coalesce and the alias. A third would mean the raw column is selected too, which
        // is what would turn one row per board into one row per job page.
        System.Text.RegularExpressions.Regex.Matches(sql, "url_query").Should().HaveCount(2);
    }

    /// <summary>A source that named no query key must not pay for a column, and must not read one either.</summary>
    [Test]
    public void BoardUrls_should_project_nothing_extra_for_a_source_without_query_keys()
    {
        var sql = ParquetIndexSql.BoardUrls(new ParquetIndexQuery
        {
            Files = ["s3://x/one.parquet"],
            Targets =
            [
                new BoardIndexTarget
                {
                    SourceId = "greenhouse",
                    Tld = "io",
                    Host = "boards-api.greenhouse.io",
                    PathPrefix = "/v1/boards/"
                }
            ]
        });

        sql.Should().NotContain("url_query");
    }

    [TestCase("https://career8.successfactors.com/career?company=brevardcou", "career8.successfactors.com/brevardcou")]
    [TestCase("https://career5.successfactors.eu/career?company=SAP&lang=en_US", "career5.successfactors.eu/SAP")]
    [TestCase("https://ace1950.jobs2web.com/search/", "ace1950.jobs2web.com")]
    [TestCase("https://career5.successfactors.eu/sfcareer/jobreqcareer?jobId=32385&company=kmd",
        "career5.successfactors.eu/kmd")]
    [TestCase("https://ascendlearning.jobs.hr.cloud.sap/job/Leawood-Senior-Software-Engineer-KS-66211/1415909200/",
        "ascendlearning.jobs.hr.cloud.sap")]
    public void TryParseBoardId_should_read_a_token_out_of_a_crawled_url(string url, string expected)
    {
        IBoardUrlParser parser = new SuccessFactorsBoardUrlParser();

        parser.TryParseBoardId(url, out var boardId).Should().BeTrue();
        boardId.Should().Be(expected);
    }

    [TestCase("https://career8.successfactors.com/career")]
    [TestCase("https://career8.successfactors.com/")]
    public void TryParseBoardId_should_refuse_a_url_that_names_no_tenant(string url)
    {
        IBoardUrlParser parser = new SuccessFactorsBoardUrlParser();

        parser.TryParseBoardId(url, out _).Should().BeFalse();
    }
}
