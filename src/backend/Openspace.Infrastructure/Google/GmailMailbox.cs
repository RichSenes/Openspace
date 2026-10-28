using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Openspace.Application.Google;
using Openspace.Application.Routines;

namespace Openspace.Infrastructure.Google;

/// <summary>
/// A Gmail mailbox through its API, as routines use it: read, label, archive, draft. It has
/// no way to send: drafts wait in the mailbox for the person.
/// </summary>
internal sealed partial class GmailMailbox(HttpClient http, IGoogleAccounts accounts, IOptionsMonitor<GoogleOptions> options) : IMailbox
{
    /// <summary>Of each body, what routines read at most.</summary>
    private const int MostText = 8000;

    /// <summary>Ids per page: the most Gmail gives without a second call is 500, a hundred keeps answers small.</summary>
    private const int PageSize = 100;

    private static readonly string[] Inbox = ["INBOX"];

    private readonly Dictionary<string, Dictionary<string, string>> _labels = new(StringComparer.OrdinalIgnoreCase);

    public async Task<MailPage> FindAsync(string account, string query, string? page, CancellationToken cancellationToken)
    {
        var list = await GetAsync(
            account, $"messages?q={Uri.EscapeDataString(query)}&maxResults={PageSize}{(page is null ? string.Empty : $"&pageToken={Uri.EscapeDataString(page)}")}",
            cancellationToken).ConfigureAwait(false);
        var ids = list.TryGetProperty("messages", out var found) ? found.EnumerateArray().Select(m => m.GetProperty("id").GetString()!).ToList() : [];
        return new MailPage(ids, list.TryGetProperty("nextPageToken", out var next) ? next.GetString() : null);
    }

    public async Task<MailMessage> ReadAsync(string account, string messageId, CancellationToken cancellationToken) =>
        Read(await GetAsync(account, $"messages/{Uri.EscapeDataString(messageId)}?format=full", cancellationToken).ConfigureAwait(false));

    public async Task<IReadOnlyList<MailMessage>> ReadThreadAsync(string account, string threadId, CancellationToken cancellationToken)
    {
        var thread = await GetAsync(account, $"threads/{Uri.EscapeDataString(threadId)}?format=full", cancellationToken).ConfigureAwait(false);
        return thread.TryGetProperty("messages", out var messages) ? [.. messages.EnumerateArray().Select(Read).OrderBy(m => m.Received)] : [];
    }

    public async Task LabelAsync(string account, string messageId, string label, bool archive, CancellationToken cancellationToken)
    {
        var id = await LabelIdAsync(account, label, cancellationToken).ConfigureAwait(false);
        await SendAsync(
            account, HttpMethod.Post, $"messages/{Uri.EscapeDataString(messageId)}/modify",
            new { addLabelIds = new[] { id }, removeLabelIds = archive ? Inbox : [] }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> DraftReplyAsync(string account, MailReply reply, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reply);
        var draft = await SendAsync(
            account, HttpMethod.Post, "drafts", new { message = new { raw = GoogleAccounts.Base64Url(Encoding.UTF8.GetBytes(Rfc822(reply))), threadId = reply.ThreadId } },
            cancellationToken).ConfigureAwait(false);
        return draft.GetProperty("id").GetString()!;
    }

    /// <summary>The reply as a mail file: in the same conversation, plain text in UTF-8.</summary>
    internal static string Rfc822(MailReply reply)
    {
        var text = new StringBuilder();
        text.Append("To: ").Append(Header(reply.To)).Append("\r\n");
        text.Append("Subject: =?UTF-8?B?").Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(reply.Subject))).Append("?=\r\n");
        if (!string.IsNullOrWhiteSpace(reply.InReplyTo))
        {
            text.Append("In-Reply-To: ").Append(Header(reply.InReplyTo)).Append("\r\n");
            text.Append("References: ").Append(Header(reply.InReplyTo)).Append("\r\n");
        }

        text.Append("MIME-Version: 1.0\r\nContent-Type: text/plain; charset=UTF-8\r\nContent-Transfer-Encoding: base64\r\n\r\n");
        text.Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(reply.Body.ReplaceLineEndings("\r\n")), Base64FormattingOptions.InsertLineBreaks));
        return text.ToString();
    }

    /// <summary>No line breaks in a header: they would start another one.</summary>
    private static string Header(string value) => value.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim();

    private async Task<string> LabelIdAsync(string account, string name, CancellationToken cancellationToken)
    {
        if (!_labels.TryGetValue(account, out var known))
        {
            var list = await GetAsync(account, "labels", cancellationToken).ConfigureAwait(false);
            known = list.TryGetProperty("labels", out var labels)
                ? labels.EnumerateArray().ToDictionary(l => l.GetProperty("name").GetString()!, l => l.GetProperty("id").GetString()!, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _labels[account] = known;
        }

        if (known.TryGetValue(name, out var id))
        {
            return id;
        }

        var made = await SendAsync(account, HttpMethod.Post, "labels", new { name, labelListVisibility = "labelShow", messageListVisibility = "show" }, cancellationToken)
            .ConfigureAwait(false);
        id = made.GetProperty("id").GetString()!;
        known[name] = id;
        return id;
    }

    internal static MailMessage Read(JsonElement message)
    {
        var payload = message.GetProperty("payload");
        var headers = payload.TryGetProperty("headers", out var h)
            ? h.EnumerateArray().GroupBy(x => x.GetProperty("name").GetString()!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().GetProperty("value").GetString() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string Of(string name) => headers.TryGetValue(name, out var value) ? value.Trim() : string.Empty;

        var labels = message.TryGetProperty("labelIds", out var l) ? l.EnumerateArray().Select(x => x.GetString()).ToList() : [];
        var received = message.TryGetProperty("internalDate", out var date) && long.TryParse(date.GetString(), out var ms)
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms)
            : DateTimeOffset.UnixEpoch;
        var text = TextOf(payload, "text/plain") ?? (TextOf(payload, "text/html") is { } html ? Plain(html) : string.Empty);
        return new MailMessage(
            message.GetProperty("id").GetString()!,
            message.TryGetProperty("threadId", out var thread) ? thread.GetString()! : string.Empty,
            Of("From"),
            Of("Reply-To") is { Length: > 0 } replyTo ? replyTo : Of("From"),
            Of("Subject") is { Length: > 0 } subject ? subject : "(no subject)",
            Of("Message-ID") is { Length: > 0 } messageId ? messageId : Of("Message-Id"),
            received,
            WebUtility.HtmlDecode(message.TryGetProperty("snippet", out var snippet) ? snippet.GetString() ?? string.Empty : string.Empty),
            text.Length <= MostText ? text : text[..MostText],
            labels.Contains("UNREAD"),
            headers.ContainsKey("List-Unsubscribe") || headers.ContainsKey("List-Id"),
            Of("To"),
            labels.Contains("SENT"));
    }

    /// <summary>The first part of this type, decoded: Gmail gives bodies in URL-safe base64.</summary>
    private static string? TextOf(JsonElement part, string type)
    {
        if (part.TryGetProperty("mimeType", out var mime) && string.Equals(mime.GetString(), type, StringComparison.OrdinalIgnoreCase)
            && part.TryGetProperty("body", out var body) && body.TryGetProperty("data", out var data) && data.GetString() is { Length: > 0 } encoded)
        {
            var base64 = encoded.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');
            return Encoding.UTF8.GetString(Convert.FromBase64String(base64)).Trim();
        }

        if (part.TryGetProperty("parts", out var parts))
        {
            foreach (var child in parts.EnumerateArray())
            {
                if (TextOf(child, type) is { } text)
                {
                    return text;
                }
            }
        }

        return null;
    }

    /// <summary>A page's words without its markup, styles and scripts.</summary>
    internal static string Plain(string html)
    {
        var text = Hidden().Replace(html, " ");
        text = Breaks().Replace(text, "\n");
        text = WebUtility.HtmlDecode(Tags().Replace(text, " "));
        return Blank().Replace(LineEdges().Replace(Spaces().Replace(text, " "), "\n"), "\n\n").Trim();
    }

    private async Task<JsonElement> GetAsync(string account, string path, CancellationToken cancellationToken) =>
        await SendAsync(account, HttpMethod.Get, path, null, cancellationToken).ConfigureAwait(false);

    private async Task<JsonElement> SendAsync(string account, HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(options.CurrentValue.GmailUri, $"gmail/v1/users/me/{path}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await accounts.AccessTokenAsync(account, cancellationToken).ConfigureAwait(false));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await GoogleAccounts.EnsureAsync(response, "Gmail", cancellationToken).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(json) ? default : JsonDocument.Parse(json).RootElement.Clone();
    }

    [GeneratedRegex(@"<(style|script|head)\b[^>]*>.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Hidden();

    [GeneratedRegex(@"<\s*(br|/p|/div|/tr|/li|/h\d)\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex Breaks();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"[ \t ]+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@" *\n *")]
    private static partial Regex LineEdges();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex Blank();
}
