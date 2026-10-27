namespace Openspace.Application.Routines;

/// <param name="Source">The outlet's name, as the routine's settings call it.</param>
/// <param name="Summary">The item's own summary, as plain text.</param>
public sealed record FeedItem(string Source, string Title, Uri Link, DateTimeOffset? Published, string Summary);

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
