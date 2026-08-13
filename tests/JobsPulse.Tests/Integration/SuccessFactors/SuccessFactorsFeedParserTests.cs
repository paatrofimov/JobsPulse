using FluentAssertions;
using JobsPulse.Sources.SuccessFactors.Infrastructure;
using NUnit.Framework;

namespace JobsPulse.Tests.Integration.SuccessFactors;

public sealed class SuccessFactorsFeedParserTests
{
    [Test]
    public async Task ParseAsync_should_read_the_channel_and_every_item()
    {
        await using var stream = SuccessFactorsFixtures.Open("feed.rss.xml");

        var feed = await SuccessFactorsFeedParser.ParseAsync(stream, includeDescriptions: true, CancellationToken.None);

        feed.Title.Should().Be("Swiss Re Careers");
        feed.Language.Should().Be("en_GB");
        feed.Items.Should().HaveCount(3);

        var first = feed.Items[0];

        first.Id.Should().Be("1408154933");
        first.Title.Should().Be("Client Manager (Kuala Lumpur, MY)");
        first.Location.Should().Be("Kuala Lumpur, MY");
        first.Link.Should().StartWith("https://careers.swissre.com/job/");
        first.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Test]
    public async Task ParseAsync_should_skip_descriptions_when_they_are_not_asked_for()
    {
        await using var stream = SuccessFactorsFixtures.Open("feed.rss.xml");

        var feed = await SuccessFactorsFeedParser.ParseAsync(stream, includeDescriptions: false, CancellationToken.None);

        feed.Items.Should().HaveCount(3);
        feed.Items.Should().OnlyContain(i => i.Description == null);
    }

    /// <summary>
    /// A site answering with a page, or with the seo url list under the name we asked the feed under, has to be told
    /// apart from a feed that was cut off - the first has nothing to fall back to, the second has.
    /// </summary>
    [Test]
    public async Task ParseAsync_should_refuse_a_document_that_is_not_a_feed()
    {
        await using var stream = SuccessFactorsFixtures.Open("sitemap.urlset.xml");

        var parse = async () =>
            await SuccessFactorsFeedParser.ParseAsync(stream, includeDescriptions: false, CancellationToken.None);

        await parse.Should().ThrowAsync<InvalidDataException>().WithMessage("*urlset*");
    }

    [Test]
    public async Task ParseAsync_should_throw_on_a_feed_that_ends_mid_document()
    {
        var whole = SuccessFactorsFixtures.Read("feed.rss.xml");

        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(whole[..(whole.Length / 2)]));

        var parse = async () =>
            await SuccessFactorsFeedParser.ParseAsync(stream, includeDescriptions: false, CancellationToken.None);

        await parse.Should().ThrowAsync<System.Xml.XmlException>();
    }
}
