using FluentAssertions;
using JobsPulse.Sources.SuccessFactors.Infrastructure;
using JobsPulse.Sources.SuccessFactors.Models;
using NUnit.Framework;

namespace JobsPulse.Tests.Integration.SuccessFactors;

public sealed class SuccessFactorsBoardUrlTests
{
    /// <summary>Every form of one board has to collapse onto one board id, or the same board is watched twice.</summary>
    [TestCase("https://jobs.sap.com")]
    [TestCase("https://jobs.sap.com/")]
    [TestCase("https://jobs.sap.com/search/?q=&sortColumn=referencedate")]
    [TestCase("https://jobs.sap.com/job/Prague-5-Senior-UX-Designer-158-00/1419687233/")]
    [TestCase("https://jobs.sap.com/go/Engineering-Jobs/1234/")]
    [TestCase("jobs.sap.com")]
    [TestCase("HTTPS://JOBS.SAP.COM/search/")]
    public void Parse_should_collapse_every_branded_form_onto_the_domain(string url)
    {
        var parts = SuccessFactorsBoardUrl.Parse(url);

        parts.Should().NotBeNull();
        parts!.Variant.Should().Be(SuccessFactorsSiteVariant.CareerSiteBuilder);
        parts.ToConfig().BoardId.Should().Be("jobs.sap.com");
    }

    /// <summary>
    /// A site mounted under a path serves every one of its routes from the domain root as well, so the path must not
    /// become part of the identity - otherwise one board gets two board ids.
    /// </summary>
    [Test]
    public void Parse_should_ignore_the_path_a_site_is_mounted_under()
    {
        var parts = SuccessFactorsBoardUrl.Parse(
            "https://jobs.aldi-sued.de/Karriere/job/Siegburg-Studentenjob-Verkauf-NW-53721/1395914533/");

        parts!.ToConfig().BoardId.Should().Be("jobs.aldi-sued.de");
        parts.IsJobUrl.Should().BeTrue();
        parts.PostId.Should().Be("1395914533");
    }

    /// <summary>
    /// A site the platform hosts itself sits under a data center domain, so the one thing that must not happen is it
    /// being read as a tenant url: nothing on it names a tenant, and the domain already is the board.
    /// </summary>
    [TestCase("https://ascendlearning.jobs.hr.cloud.sap/job/Leawood-Senior-Software-Engineer-KS-66211/1415909200/",
        "ascendlearning.jobs.hr.cloud.sap", "1415909200")]
    [TestCase("https://ace1950.jobs2web.com/search/", "ace1950.jobs2web.com", null)]
    public void Parse_should_read_a_platform_hosted_site_as_the_board_itself(
        string url,
        string expected,
        string? postId)
    {
        var parts = SuccessFactorsBoardUrl.Parse(url);

        parts.Should().NotBeNull();
        parts!.Variant.Should().Be(SuccessFactorsSiteVariant.CareerSiteBuilder);
        parts.ToConfig().BoardId.Should().Be(expected);
        parts.PostId.Should().Be(postId);
    }

    [TestCase("https://career8.successfactors.com/career?company=brevardcou", "career8.successfactors.com/brevardcou")]
    [TestCase("https://career5.successfactors.eu/career?company=SAP&lang=en_US", "career5.successfactors.eu/SAP")]
    [TestCase("https://career41.sapsf.com/career?career_company=joysonsafety", "career41.sapsf.com/joysonsafety")]
    [TestCase("https://career-hcm20.ns2cloud.com/career?company=ENTHCM20", "career-hcm20.ns2cloud.com/ENTHCM20")]
    // The deep link to one posting - the form a vacancy is actually shared as, and a different path of the same board.
    [TestCase("https://career5.successfactors.eu/sfcareer/jobreqcareer?jobId=32385&company=kmd&utm_source=chatgpt.com",
        "career5.successfactors.eu/kmd")]
    public void Parse_should_read_the_tenant_out_of_a_legacy_url(string url, string expected)
    {
        var parts = SuccessFactorsBoardUrl.Parse(url);

        parts.Should().NotBeNull();
        parts!.Variant.Should().Be(SuccessFactorsSiteVariant.LegacyCareerPortal);
        parts.ToConfig().BoardId.Should().Be(expected);
    }

    /// <summary>A data center url with no tenant names nothing there is to look up.</summary>
    [TestCase("https://career8.successfactors.com/career")]
    [TestCase("https://career4.successfactors.com/careers")]
    [TestCase("https://jobs.sap.com/about/our-culture")]
    [TestCase("not a url at all")]
    [TestCase("")]
    [TestCase("ftp://jobs.sap.com/search/")]
    public void Parse_should_return_null_for_what_is_not_a_board(string url)
    {
        SuccessFactorsBoardUrl.Parse(url).Should().BeNull();
    }

    /// <summary>A deep link names the requisition, and it is not the 'career_job_req_id' the portal url uses.</summary>
    [Test]
    public void Parse_should_recognize_a_legacy_deep_link_as_a_job_url()
    {
        SuccessFactorsBoardUrl
            .Parse("https://career5.successfactors.eu/sfcareer/jobreqcareer?jobId=32385&company=kmd")!
            .IsJobUrl.Should().BeTrue();
    }

    [Test]
    public void FromBoardId_should_round_trip_both_forms()
    {
        SuccessFactorsBoardConfig.FromBoardId("jobs.sap.com")!.Domain.Should().Be("jobs.sap.com");

        var legacy = SuccessFactorsBoardConfig.FromBoardId("career8.successfactors.com/brevardcou")!;

        legacy.RcmHost.Should().Be("career8.successfactors.com");
        legacy.Tenant.Should().Be("brevardcou");
        legacy.Variant.Should().Be(SuccessFactorsSiteVariant.LegacyCareerPortal);
        legacy.BoardId.Should().Be("career8.successfactors.com/brevardcou");
    }

    [Test]
    public void FromJson_should_round_trip_a_configuration()
    {
        var config = new SuccessFactorsBoardConfig
        {
            Domain = "jobs.sap.com",
            Tenant = "SAP",
            RcmHost = "career5.successfactors.eu",
            Locale = "en_US"
        };

        var restored = SuccessFactorsBoardConfig.FromJson(config.ToJson());

        restored.Should().BeEquivalentTo(config);
        restored!.BoardId.Should().Be("jobs.sap.com");
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("{ not json")]
    [TestCase("{}")]
    public void FromJson_should_return_null_for_an_unusable_configuration(string json)
    {
        SuccessFactorsBoardConfig.FromJson(json).Should().BeNull();
    }
}
