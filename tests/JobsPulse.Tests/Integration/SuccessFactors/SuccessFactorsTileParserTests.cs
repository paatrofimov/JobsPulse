using FluentAssertions;
using JobsPulse.Sources.SuccessFactors.Infrastructure;
using NUnit.Framework;

namespace JobsPulse.Tests.Integration.SuccessFactors;

public sealed class SuccessFactorsTileParserTests
{
    /// <summary>
    /// The fixture is SAP's own listing, and SAP renders nothing on a tile but the title - which is exactly the case
    /// the parser has to survive, because what a tile carries is configured per customer.
    /// </summary>
    [Test]
    public void Parse_should_read_the_id_the_url_and_the_title()
    {
        var tiles = SuccessFactorsTileParser.Parse(SuccessFactorsFixtures.Read("tiles.html"));

        tiles.Should().NotBeEmpty();
        tiles.Should().OnlyContain(t => t.Id.Length > 0 && t.Url.Length > 0);
        tiles.Should().OnlyContain(t => t.Url.Contains("/job/"));

        var first = tiles[0];

        first.Id.Should().Be("1404567933");
        first.Title.Should().Be("SAP iXp - Global Value Advisory");
    }

    [Test]
    public void Parse_should_return_nothing_for_a_page_with_no_tiles()
    {
        SuccessFactorsTileParser.Parse("<html><body>no jobs here</body></html>").Should().BeEmpty();
        SuccessFactorsTileParser.Parse("").Should().BeEmpty();
    }
}
