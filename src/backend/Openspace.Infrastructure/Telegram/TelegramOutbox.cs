using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Openspace.Application.Routines;
using Openspace.Domain.Routines;

namespace Openspace.Infrastructure.Telegram;

public sealed class TelegramOptions
{
    public const string SectionName = "Telegram";

    /// <summary>
    /// The person's own folder with <c>telegram.env</c> (one bot token per line, NAME=token)
    /// and <c>telegram.json</c> (<c>{"chat": id, "topics": {"NAME": thread}}</c>). Never in a repository.
    /// </summary>
    public string Folder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "openspace");
}

/// <summary>
/// Delivers what routines make to the person's Telegram group: each routine names its bot in
/// its settings (<c>"channel": "KAWARABAN"</c>) and writes in that bot's topic. Routines that
/// name none deliver nothing.
/// </summary>
internal sealed class TelegramOutbox(HttpClient http, IOptionsMonitor<TelegramOptions> options) : IRoutineOutbox
{
    /// <summary>Telegram takes at most 4096 characters per message; a margin for safety.</summary>
    private const int MaxText = 4000;

    private const int MaxCaption = 1000;

    public async Task<bool> SendAsync(Routine routine, string text, string? filePath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(routine);
        if (ChannelOf(routine.Settings) is not { } channel)
        {
            return false;
        }

        var (token, chat, topic) = Read(channel);
        if (filePath is not null)
        {
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(chat), "chat_id");
            if (topic is not null)
            {
                form.Add(new StringContent(topic), "message_thread_id");
            }

            form.Add(new StringContent(text.Length <= MaxCaption ? text : string.Concat(text.AsSpan(0, MaxCaption - 1), "…")), "caption");
            await using var file = File.OpenRead(filePath);
            var content = new StreamContent(file);
            content.Headers.ContentType = new MediaTypeHeaderValue(filePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? "application/pdf" : "application/octet-stream");
            form.Add(content, "document", Path.GetFileName(filePath));
            await Bots.CallAsync(http, token, "sendDocument", form, cancellationToken).ConfigureAwait(false);
            return true;
        }

        foreach (var chunk in Chunks(text))
        {
            var fields = new Dictionary<string, string> { ["chat_id"] = chat, ["text"] = chunk };
            if (topic is not null)
            {
                fields["message_thread_id"] = topic;
            }

            using var form = new FormUrlEncodedContent(fields);
            await Bots.CallAsync(http, token, "sendMessage", form, cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    public async Task<long?> PostAsync(Routine routine, OutboxMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(routine);
        ArgumentNullException.ThrowIfNull(message);
        if (ChannelOf(routine.Settings) is not { } channel)
        {
            return null;
        }

        var (token, chat, topic) = Read(channel);
        var most = message.FilePath is null ? MaxText : MaxCaption;
        var html = message.Html.Length <= most;
        var fields = new Dictionary<string, string>
        {
            ["chat_id"] = chat,
            [message.FilePath is null ? "text" : "caption"] = html ? message.Html : Cut(message.Plain, most),
        };
        if (html)
        {
            fields["parse_mode"] = "HTML";
        }

        if (topic is not null)
        {
            fields["message_thread_id"] = topic;
        }

        if (message.ReplyTo is { } replyTo)
        {
            // When the message is gone the answer still arrives, on its own.
            fields["reply_parameters"] = JsonSerializer.Serialize(new { message_id = replyTo, allow_sending_without_reply = true });
        }

        if (message.Buttons is { Count: > 0 } buttons)
        {
            fields["reply_markup"] = Keyboard(buttons);
        }

        JsonElement answer;
        if (message.FilePath is { } path)
        {
            using var form = new MultipartFormDataContent();
            foreach (var (name, value) in fields)
            {
                form.Add(new StringContent(value), name);
            }

            await using var file = File.OpenRead(path);
            var content = new StreamContent(file);
            content.Headers.ContentType = new MediaTypeHeaderValue(path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? "application/pdf" : "application/octet-stream");
            form.Add(content, "document", Path.GetFileName(path));
            answer = await Bots.CallAsync(http, token, "sendDocument", form, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            fields["link_preview_options"] = """{"is_disabled":true}""";
            using var form = new FormUrlEncodedContent(fields);
            answer = await Bots.CallAsync(http, token, "sendMessage", form, cancellationToken).ConfigureAwait(false);
        }

        return answer.ValueKind == JsonValueKind.Object && answer.TryGetProperty("result", out var result) && result.TryGetProperty("message_id", out var id) && id.TryGetInt64(out var number)
            ? number
            : 0;
    }

    /// <summary>Buttons side by side under a message, in Telegram's shape.</summary>
    internal static string Keyboard(IEnumerable<OutboxButton> buttons) =>
        JsonSerializer.Serialize(new { inline_keyboard = new[] { buttons.Select(b => new { text = b.Text, callback_data = b.Data }).ToArray() } }, Plain);

    /// <summary>JSON with its text as written: it goes in a form, never in a page.</summary>
    private static readonly JsonSerializerOptions Plain = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static string Cut(string text, int most) => text.Length <= most ? text : string.Concat(text.AsSpan(0, most - 1), "…");

    /// <summary>The bots named in <c>telegram.env</c>: the names before the "=", never the tokens.</summary>
    public IReadOnlyList<string> Channels()
    {
        var tokens = Path.Combine(options.CurrentValue.Folder, "telegram.env");
        return File.Exists(tokens) ? NamesIn(File.ReadLines(tokens)) : [];
    }

    internal static IReadOnlyList<string> NamesIn(IEnumerable<string> lines) =>
    [
        .. lines.Select(line => line.Trim())
            .Where(line => !line.StartsWith('#') && line.IndexOf('=', StringComparison.Ordinal) > 0)
            .Select(line => line[..line.IndexOf('=', StringComparison.Ordinal)].Trim().ToUpperInvariant())
            .Distinct()
            .Order(StringComparer.Ordinal),
    ];

    internal static string? ChannelOf(string settings)
    {
        using var document = JsonDocument.Parse(settings);
        return document.RootElement.TryGetProperty("channel", out var channel) && channel.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(channel.GetString())
            ? channel.GetString()!.Trim().ToUpperInvariant()
            : null;
    }

    /// <summary>Splits long text on line breaks, so no message is cut in the middle of a line.</summary>
    internal static IEnumerable<string> Chunks(string text)
    {
        var rest = text.Trim();
        while (rest.Length > MaxText)
        {
            var cut = rest.LastIndexOf('\n', MaxText);
            cut = cut > MaxText / 2 ? cut : MaxText;
            yield return rest[..cut].TrimEnd();
            rest = rest[cut..].TrimStart();
        }

        if (rest.Length > 0)
        {
            yield return rest;
        }
    }

    private (string Token, string Chat, string? Topic) Read(string channel) => Bots.Read(options.CurrentValue.Folder, channel);
}

/// <summary>The person's bots: each one's token, the group chat and the bot's topic in it.</summary>
internal static class Bots
{
    public static (string Token, string Chat, string? Topic) Read(string folder, string channel)
    {
        var tokens = Path.Combine(folder, "telegram.env");
        var targets = Path.Combine(folder, "telegram.json");
        if (!File.Exists(tokens) || !File.Exists(targets))
        {
            throw new InvalidOperationException($"Telegram is not set up: {tokens} and {targets} are needed.");
        }

        var token = File.ReadLines(tokens)
            .Select(line => line.Trim())
            .Where(line => !line.StartsWith('#') && line.Contains('=', StringComparison.Ordinal))
            .Select(line => (Name: line[..line.IndexOf('=', StringComparison.Ordinal)].Trim().ToUpperInvariant(), Value: line[(line.IndexOf('=', StringComparison.Ordinal) + 1)..].Trim()))
            .FirstOrDefault(pair => pair.Name == channel).Value
            ?? throw new InvalidOperationException($"No Telegram bot called {channel} in {tokens}.");

        using var document = JsonDocument.Parse(File.ReadAllText(targets));
        var root = document.RootElement;
        var chat = root.GetProperty("chat").GetRawText();
        string? topic = root.TryGetProperty("topics", out var topics) && topics.TryGetProperty(channel, out var thread) ? thread.GetRawText() : null;
        return (token, chat, topic);
    }

    /// <summary>Calls the bot; Telegram's answer, its "result" holding what was sent.</summary>
    public static async Task<JsonElement> CallAsync(HttpClient http, string token, string method, HttpContent content, CancellationToken cancellationToken)
    {
        // "./": a token has a colon, which would otherwise read as a scheme.
        using var response = await http.PostAsync(new Uri($"./bot{token}/{method}", UriKind.Relative), content, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            // Telegram says what went wrong in the body; the token never goes into the message.
            var description = JsonDocument.Parse(body).RootElement.TryGetProperty("description", out var d) ? d.GetString() : body;
            throw new HttpRequestException($"Telegram refused the message: {description}");
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return default;
        }
    }
}
