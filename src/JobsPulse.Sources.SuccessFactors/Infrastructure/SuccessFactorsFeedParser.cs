using System.Xml;
using JobsPulse.Sources.SuccessFactors.Models;

namespace JobsPulse.Sources.SuccessFactors.Infrastructure;

/// <summary>
/// Reads the career site feed as a stream rather than a document. A board of a few thousand vacancies is tens of
/// megabytes of embedded html, and none of it has to be held at once: items are emitted as they arrive and a
/// description that nobody asked for is skipped without ever becoming a string.
///
/// Element names are matched on the local name alone. The feed binds the aggregator fields to the google base
/// namespace, but a prefix is not a contract and some sites emit the same fields unbound; the names do not collide
/// with the rss ones, so the namespace buys nothing and would only be one more way to drop a whole board.
/// </summary>
public static class SuccessFactorsFeedParser
{
    /// <exception cref="InvalidDataException">The body is well formed but is not a feed - an error page, or the seo
    /// url list that some sites answer '/sitemap.xml' with.</exception>
    /// <exception cref="XmlException">The body ends mid-way, or is not xml at all.</exception>
    public static async Task<JobFeedDto> ParseAsync(Stream stream, bool includeDescriptions, CancellationToken ct)
    {
        using var reader = XmlReader.Create(stream, ReaderSettings());

        // The root is checked before anything is walked, so a site answering with a page or with the seo url list is
        // told apart from a feed that was cut off - the first has nothing to retry, the second has a fallback.
        await ReadRootAsync(reader, ct);

        return await ReadChannelAsync(reader, includeDescriptions, ct);
    }

    /// <summary>
    /// The same walk, for a reader already standing on the '&lt;rss&gt;' root. The sitemap is one of two documents and
    /// has to sniff its root itself; when it turns out to be the feed, this is what reads it.
    /// </summary>
    public static async Task<JobFeedDto> ReadChannelAsync(
        XmlReader reader,
        bool includeDescriptions,
        CancellationToken ct)
    {
        string? channelTitle = null;
        string? language = null;
        var items = new List<JobFeedItemDto>();

        // Reading an element's content leaves the reader on the *next* node already, so the walk must not advance
        // again after one - doing both skips every other element, and the feed lists its fields one after another.
        while (!reader.EOF)
        {
            ct.ThrowIfCancellationRequested();

            if (reader.NodeType != XmlNodeType.Element)
            {
                if (!await reader.ReadAsync())
                    break;

                continue;
            }

            switch (reader.LocalName)
            {
                case "item":
                    items.Add(await ReadItemAsync(reader, includeDescriptions, ct));

                    // ReadSubtree leaves the outer reader on </item>; step off it before looking for the next one.
                    await reader.ReadAsync();
                    break;

                // Only a direct child of <channel> - rss is depth 0, channel 1, its children 2. An <image> or a
                // <textInput> block carries a <title> too, and it is not the name of the site.
                case "title" when reader.Depth == 2 && channelTitle is null:
                    channelTitle = Clean(await reader.ReadElementContentAsStringAsync());
                    break;

                case "language" when reader.Depth == 2 && language is null:
                    language = Clean(await reader.ReadElementContentAsStringAsync());
                    break;

                default:
                    await reader.ReadAsync();
                    break;
            }
        }

        return new JobFeedDto
        {
            Title = channelTitle,
            Language = language,
            Items = items
        };
    }

    /// <summary>The name of the root element, or null for an empty document.</summary>
    public static async Task<string?> ReadRootNameAsync(XmlReader reader, CancellationToken ct)
    {
        while (await reader.ReadAsync())
        {
            ct.ThrowIfCancellationRequested();

            if (reader.NodeType == XmlNodeType.Element)
                return reader.LocalName;
        }

        return null;
    }

    private static async Task ReadRootAsync(XmlReader reader, CancellationToken ct)
    {
        var root = await ReadRootNameAsync(reader, ct)
                   ?? throw new InvalidDataException("the document is empty");

        if (!root.Equals("rss", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"the document is <{root}>, not a job feed");
    }

    /// <summary>Settings shared by everything reading xml off a career site - see <see cref="ParseAsync"/>.</summary>
    public static XmlReaderSettings ReaderSettings() => new()
    {
        Async = true,
        DtdProcessing = DtdProcessing.Prohibit,
        IgnoreComments = true,
        IgnoreWhitespace = true,
        IgnoreProcessingInstructions = true,
        CloseInput = false
    };

    private static async Task<JobFeedItemDto> ReadItemAsync(
        XmlReader reader,
        bool includeDescriptions,
        CancellationToken ct)
    {
        string? id = null, guid = null, title = null, link = null, description = null;
        string? location = null, employer = null, jobFunction = null, expiration = null;

        // The subtree ends at </item> whatever the item holds, so an unknown element cannot desynchronize the walk.
        using var item = reader.ReadSubtree();

        // Onto <item>, then onto its first child. From here every branch leaves the reader on the next node itself.
        await item.ReadAsync();
        await item.ReadAsync();

        while (!item.EOF)
        {
            ct.ThrowIfCancellationRequested();

            if (item.NodeType != XmlNodeType.Element)
            {
                if (!await item.ReadAsync())
                    break;

                continue;
            }

            switch (item.LocalName)
            {
                case "id":
                    id = Clean(await item.ReadElementContentAsStringAsync());
                    break;

                case "guid":
                    guid = Clean(await item.ReadElementContentAsStringAsync());
                    break;

                case "title":
                    title = Clean(await item.ReadElementContentAsStringAsync());
                    break;

                case "link":
                    link = Clean(await item.ReadElementContentAsStringAsync());
                    break;

                case "description":
                    if (includeDescriptions)
                        description = Clean(await item.ReadElementContentAsStringAsync());
                    else
                        await item.SkipAsync();

                    break;

                case "location":
                    location = Clean(await item.ReadElementContentAsStringAsync());
                    break;

                case "employer":
                    employer = Clean(await item.ReadElementContentAsStringAsync());
                    break;

                case "job_function":
                    jobFunction = Clean(await item.ReadElementContentAsStringAsync());
                    break;

                case "expiration_date":
                    expiration = Clean(await item.ReadElementContentAsStringAsync());
                    break;

                // An element the feed grew since this was written, or one holding markup rather than text. Skipping
                // it costs one field; reading past it would cost the rest of the item.
                default:
                    await item.SkipAsync();
                    break;
            }
        }

        return new JobFeedItemDto
        {
            // <g:id> and <guid> are the same number; either alone is enough to identify the posting.
            Id = id ?? guid,
            Title = title,
            Link = link,
            Description = description,
            Location = location,
            Employer = employer,
            JobFunction = jobFunction,
            ExpirationDate = expiration
        };
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
