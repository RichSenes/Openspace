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
            if (bytes.Length is <= 2_000 or > MaxBytes)
            {
                return null;
            }

            var (width, height) = Size(bytes);
            return new WebImage(bytes, type, width, height);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>A picture's size from its own header: PNG, GIF, WebP or JPEG; (0, 0) when unknown.</summary>
    internal static (int Width, int Height) Size(ReadOnlySpan<byte> b)
    {
        static int Be16(ReadOnlySpan<byte> s) => (s[0] << 8) | s[1];
        static int Le16(ReadOnlySpan<byte> s) => s[0] | (s[1] << 8);
        static int Le24(ReadOnlySpan<byte> s) => s[0] | (s[1] << 8) | (s[2] << 16);

        if (b.Length >= 24 && b[0] == 0x89 && b[1] == (byte)'P' && b[2] == (byte)'N' && b[3] == (byte)'G')
        {
            return ((b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19], (b[20] << 24) | (b[21] << 16) | (b[22] << 8) | b[23]);
        }

        if (b.Length >= 10 && b[0] == (byte)'G' && b[1] == (byte)'I' && b[2] == (byte)'F')
        {
            return (Le16(b[6..]), Le16(b[8..]));
        }

        if (b.Length >= 30 && b[0] == (byte)'R' && b[8] == (byte)'W' && b[9] == (byte)'E' && b[10] == (byte)'B')
        {
            var chunk = System.Text.Encoding.ASCII.GetString(b[12..16]);
            return chunk switch
            {
                "VP8 " => (Le16(b[26..]) & 0x3FFF, Le16(b[28..]) & 0x3FFF),
                "VP8L" => (1 + (b[21] | ((b[22] & 0x3F) << 8)), 1 + ((b[22] >> 6) | (b[23] << 2) | ((b[24] & 0x0F) << 10))),
                "VP8X" => (1 + Le24(b[24..]), 1 + Le24(b[27..])),
                _ => (0, 0),
            };
        }

        if (b.Length >= 4 && b[0] == 0xFF && b[1] == 0xD8)
        {
            // JPEG: walk the segments to the frame header (SOF0 to SOF15, but not DHT, JPG, DAC).
            var i = 2;
            while (i + 9 < b.Length)
            {
                if (b[i] != 0xFF)
                {
                    i++;
                    continue;
                }

                var marker = b[i + 1];
                if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
                {
                    return (Be16(b[(i + 7)..]), Be16(b[(i + 5)..]));
                }

                i += 2 + Be16(b[(i + 2)..]);
            }
        }

        return (0, 0);
    }

    [GeneratedRegex(@"<meta\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex Meta();

    [GeneratedRegex(@"(property|name)\s*=\s*[""'](og:image|og:image:url|twitter:image)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex OgImage();

    [GeneratedRegex(@"content\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex Content();
}
