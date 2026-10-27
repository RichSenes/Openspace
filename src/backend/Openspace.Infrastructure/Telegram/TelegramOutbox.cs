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
            await PostAsync(token, "sendDocument", form, cancellationToken).ConfigureAwait(false);
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
            await PostAsync(token, "sendMessage", form, cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

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

    private (string Token, string Chat, string? Topic) Read(string channel)
    {
        var folder = options.CurrentValue.Folder;
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

    private async Task PostAsync(string token, string method, HttpContent content, CancellationToken cancellationToken)
    {
        // "./": a token has a colon, which would otherwise read as a scheme.
        using var response = await http.PostAsync(new Uri($"./bot{token}/{method}", UriKind.Relative), content, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            // Telegram says what went wrong in the body; the token never goes into the message.
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var description = JsonDocument.Parse(body).RootElement.TryGetProperty("description", out var d) ? d.GetString() : body;
            throw new HttpRequestException($"Telegram refused the message: {description}");
        }
    }
}
