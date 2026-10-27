using System.Net;
using System.Text.RegularExpressions;
using Openspace.Application.Routines;

namespace Openspace.Infrastructure.Routines;

/// <summary>Finds and downloads an article's own picture, honestly identified, small enough to embed.</summary>
internal sealed partial class WebImages(HttpClient http) : IWebImages
{
    /// <summary>Pictures bigger than this are left out: a paper is not a gallery.</summary>
    private const int MaxBytes = 3 * 1024 * 1024;

    /// <summary>Pages are read only up to their head, where the picture is declared.</summary>
    private const int MaxPage = 300_000;

    private static readonly string[] Types = ["image/jpeg", "image/png", "image/webp", "image/gif"];

    public async Task<Uri?> PictureOfAsync(Uri page, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.GetAsync(page, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var buffer = new char[MaxPage];
            using var reader = new StreamReader(stream);
            var read = await reader.ReadBlockAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            return Declared(new string(buffer, 0, read), page);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    internal static Uri? Declared(string html, Uri page)
    {
        foreach (Match meta in Meta().Matches(html))
        {
            var tag = meta.Value;
            if (!OgImage().IsMatch(tag) || Content().Match(tag) is not { Success: true } content)
            {
                continue;
            }

            if (Uri.TryCreate(page, WebUtility.HtmlDecode(content.Groups[1].Value).Trim(), out var picture) && picture.Scheme is "http" or "https")
            {
                return picture;
            }
        }

        return null;
    }

    public async Task<WebImage?> DownloadAsync(Uri picture, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.GetAsync(picture, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            var type = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();
            if (!response.IsSuccessStatusCode || type is null || !Types.Contains(type) || response.Content.Headers.ContentLength > MaxBytes)
            {
                return null;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            return bytes.Length is > 2_000 and <= MaxBytes ? new WebImage(bytes, type) : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    [GeneratedRegex(@"<meta\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex Meta();

    [GeneratedRegex(@"(property|name)\s*=\s*[""'](og:image|og:image:url|twitter:image)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex OgImage();

    [GeneratedRegex(@"content\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex Content();
}
