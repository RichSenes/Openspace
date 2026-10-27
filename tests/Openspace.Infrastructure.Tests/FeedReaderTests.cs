using System.Text;
using Openspace.Infrastructure.Routines;

namespace Openspace.Infrastructure.Tests;

public class FeedReaderTests
{
    private static readonly Uri Feed = new("https://news.example/feed");

    private static IReadOnlyList<Application.Routines.FeedItem> Parse(string xml) =>
        FeedReader.Parse(new MemoryStream(Encoding.UTF8.GetBytes(xml)), Feed, "Example News");

    [Fact]
    public void Rss_items_come_with_plain_text_summaries_and_dates()
    {
        var items = Parse("""
            <rss version="2.0"><channel><title>Example</title>
              <item><title>Kyoto &amp; the snow</title><link>https://news.example/kyoto</link>
                <pubDate>Tue, 27 Oct 2026 07:00:00 +0100</pubDate>
                <description><![CDATA[<p>First <b>snow</b>&nbsp;of the year.</p>]]></description></item>
              <item><title>Relative link</title><link>/local</link><pubDate>Mon, 26 Oct 2026 10:00:00 GMT</pubDate></item>
              <item><title></title><link>https://news.example/untitled</link></item>
            </channel></rss>
            """);

        Assert.Equal(2, items.Count);
        Assert.Equal(("Kyoto & the snow", "First snow of the year."), (items[0].Title, items[0].Summary));
        Assert.Equal(new DateTimeOffset(2026, 10, 27, 6, 0, 0, TimeSpan.Zero), items[0].Published!.Value.ToUniversalTime());
        Assert.Equal(("https://news.example/local", "Example News"), (items[1].Link.ToString(), items[1].Source));
        Assert.Equal(new DateTimeOffset(2026, 10, 26, 10, 0, 0, TimeSpan.Zero), items[1].Published);
    }

    [Fact]
    public void Atom_entries_take_their_alternate_link()
    {
        var items = Parse("""
            <feed xmlns="http://www.w3.org/2005/Atom"><title>Lab</title>
              <entry><title>A new open model</title>
                <link rel="self" href="https://news.example/self"/><link href="https://news.example/model"/>
                <updated>2026-10-27T05:30:00Z</updated><summary>Weights are out.</summary></entry>
            </feed>
            """);

        var item = Assert.Single(items);
        Assert.Equal(("https://news.example/model", "Weights are out."), (item.Link.ToString(), item.Summary));
    }

    [Fact]
    public void A_feed_cannot_make_the_reader_open_local_files()
    {
        // Declarations are skipped, so the entity is unknown: the feed is refused, nothing is read.
        Assert.Throws<System.Xml.XmlException>(() => Parse("""
            <?xml version="1.0"?>
            <!DOCTYPE rss [<!ENTITY secret SYSTEM "file:///etc/hostname">]>
            <rss version="2.0"><channel><item><title>Leak &secret;</title><link>https://news.example/x</link></item></channel></rss>
            """));
    }

    [Fact]
    public void Links_that_are_not_web_pages_are_left_out()
    {
        Assert.Empty(Parse("""<rss><channel><item><title>Script</title><link>javascript:alert(1)</link></item></channel></rss>"""));
    }

    [Fact]
    public void Pictures_come_from_media_rss_enclosures_or_the_item_s_html()
    {
        var items = Parse("""
            <rss version="2.0" xmlns:media="http://search.yahoo.com/mrss/"><channel>
              <item><title>One</title><link>https://news.example/1</link><media:content url="https://img.example/1.jpg" medium="image"/></item>
              <item><title>Two</title><link>https://news.example/2</link><enclosure url="https://img.example/2.png" type="image/png"/></item>
              <item><title>Three</title><link>https://news.example/3</link><description><![CDATA[<p><img src="/pics/3.webp"> text</p>]]></description></item>
              <item><title>Four</title><link>https://news.example/4</link></item>
            </channel></rss>
            """);

        Assert.Equal(
            ["https://img.example/1.jpg", "https://img.example/2.png", "https://news.example/pics/3.webp", null],
            items.Select(i => i.Image?.ToString()));
    }

    [Fact]
    public void A_page_s_own_picture_is_its_open_graph_image()
    {
        var page = new Uri("https://news.example/story");

        Assert.Equal(
            "https://news.example/img/lead.jpg",
            WebImages.Declared("""<head><meta name="x" content="y"><meta property="og:image" content="/img/lead.jpg"></head>""", page)?.ToString());
        Assert.Equal(
            "https://cdn.example/a.png",
            WebImages.Declared("""<meta content="https://cdn.example/a.png" name="twitter:image">""", page)?.ToString());
        Assert.Null(WebImages.Declared("""<meta property="og:image" content="javascript:alert(1)">""", page));
    }

    [Fact]
    public void A_picture_s_size_is_read_from_its_own_header()
    {
        byte[] png = [0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R', 0, 0, 2, 188, 0, 0, 7, 71];
        byte[] gif = [(byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a', 0x20, 0x03, 0x58, 0x02];
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 4, 0, 0, 0xFF, 0xC0, 0, 17, 8, 0x02, 0xA3, 0x04, 0xB0, 3, 0, 0, 0, 0, 0, 0];

        Assert.Equal((700, 1863), WebImages.Size(png));
        Assert.Equal((800, 600), WebImages.Size(gif));
        Assert.Equal((1200, 675), WebImages.Size(jpeg));
        Assert.Equal((0, 0), WebImages.Size([1, 2, 3]));
        Assert.False(new Application.Routines.WebImage(png, "image/png", 700, 1863).FitsAFrame);
        Assert.False(new Application.Routines.WebImage(jpeg, "image/jpeg", 320, 180).FitsAFrame);
        Assert.True(new Application.Routines.WebImage(jpeg, "image/jpeg", 1200, 675).FitsAFrame);
    }
}
