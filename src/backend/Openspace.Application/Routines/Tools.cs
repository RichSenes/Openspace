namespace Openspace.Application.Routines;

/// <param name="Source">The outlet's name, as the routine's settings call it.</param>
/// <param name="Summary">The item's own summary, as plain text.</param>
/// <param name="Image">The picture the feed gives with it, if any.</param>
public sealed record FeedItem(string Source, string Title, Uri Link, DateTimeOffset? Published, string Summary, Uri? Image = null);

/// <summary>Reads news feeds (RSS or Atom) for routines.</summary>
public interface IFeedReader
{
    Task<IReadOnlyList<FeedItem>> ReadAsync(Uri feed, string source, CancellationToken cancellationToken);
}

/// <summary>Turns a page into a PDF, as a browser prints it.</summary>
public interface IPdfPrinter
{
    Task<byte[]> PrintAsync(string html, CancellationToken cancellationToken);
}

/// <param name="MediaType">"image/jpeg", "image/png", "image/webp" or "image/gif".</param>
public sealed record WebImage(byte[] Bytes, string MediaType)
{
    public string DataUri => $"data:{MediaType};base64,{Convert.ToBase64String(Bytes)}";
}

/// <summary>Pictures for routines' pages: an article's own main picture, downloaded to embed.</summary>
public interface IWebImages
{
    /// <summary>The main picture a web page declares for itself (its Open Graph image); null when none.</summary>
    Task<Uri?> PictureOfAsync(Uri page, CancellationToken cancellationToken);

    /// <summary>The picture itself, when it is a web image of a reasonable size; null otherwise.</summary>
    Task<WebImage?> DownloadAsync(Uri picture, CancellationToken cancellationToken);
}
