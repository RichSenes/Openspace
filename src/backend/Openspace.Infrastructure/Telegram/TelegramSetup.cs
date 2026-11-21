using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Openspace.Application;
using Openspace.Application.Telegram;

namespace Openspace.Infrastructure.Telegram;

/// <summary>
/// Keeps the person's bots in <c>telegram.env</c> and the group with its topics in
/// <c>telegram.json</c>, the files the outbox and the listener read. Both are written whole,
/// readable by the person only; tokens are written and never read back out to anyone.
/// </summary>
internal sealed partial class TelegramSetup(HttpClient http, IOptionsMonitor<TelegramOptions> options) : ITelegramSetup
{
    public const string TokensFile = "telegram.env";

    public const string TargetsFile = "telegram.json";

    private static readonly SemaphoreSlim Writing = new(1, 1);

    private string Folder => options.CurrentValue.Folder;

    private string Tokens => Path.Combine(Folder, TokensFile);

    private string Targets => Path.Combine(Folder, TargetsFile);

    public Task<TelegramStatusView> StatusAsync(CancellationToken cancellationToken)
    {
        var names = File.Exists(Tokens) ? TelegramOutbox.NamesIn(File.ReadLines(Tokens)) : [];
        var targets = ReadTargets();
        var topics = targets["topics"] as JsonObject;
        return Task.FromResult(new TelegramStatusView(
            Folder,
            NumberOf(targets["chat"]),
            [.. names.Select(n => new TelegramBotView(n, NumberOf(topics?[n])))]));
    }

    public async Task<string> AddBotAsync(string name, string token, long? topic, CancellationToken cancellationToken)
    {
        var bot = ValidName(name);
        var key = token?.Trim() ?? string.Empty;
        if (!TokenPattern().IsMatch(key))
        {
            throw new RejectedException("telegram-token", "That does not look like a bot token: BotFather gives one as a number, a colon and a long code.");
        }

        var me = await CallAsync(key, "getMe", new FormUrlEncodedContent([]), "Telegram did not accept this token", cancellationToken).ConfigureAwait(false);
        var username = me.TryGetProperty("result", out var result) && result.TryGetProperty("username", out var u) ? u.GetString() ?? bot : bot;

        await Writing.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var lines = File.Exists(Tokens) ? File.ReadAllLines(Tokens).ToList() : ["# Telegram bots for Openspace routines: NAME=token. Keep this file private."];
            lines.RemoveAll(line => NameOf(line) == bot);
            lines.Add($"{bot}={key}");
            WritePrivate(Tokens, string.Join('\n', lines) + "\n");
            var targets = ReadTargets();
            Topics(targets).Remove(bot);
            if (topic is { } t)
            {
                Topics(targets)[bot] = t;
            }

            WritePrivate(Targets, targets.ToJsonString(Indented));
        }
        finally
        {
            Writing.Release();
        }

        return username;
    }

    public async Task RemoveBotAsync(string name, CancellationToken cancellationToken)
    {
        var bot = ValidName(name);
        await Writing.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(Tokens))
            {
                var lines = File.ReadAllLines(Tokens).ToList();
                if (lines.RemoveAll(line => NameOf(line) == bot) > 0)
                {
                    WritePrivate(Tokens, string.Join('\n', lines) + "\n");
                }
            }

            if (File.Exists(Targets))
            {
                var targets = ReadTargets();
                if (Topics(targets).Remove(bot))
                {
                    WritePrivate(Targets, targets.ToJsonString(Indented));
                }
            }
        }
        finally
        {
            Writing.Release();
        }
    }

    public async Task SetChatAsync(long chat, CancellationToken cancellationToken)
    {
        if (chat == 0)
        {
            throw new RejectedException("telegram-chat", "A group's number is never 0: find it with one of your bots, or copy it from Telegram.");
        }

        await Writing.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var targets = ReadTargets();
            targets["chat"] = chat;
            WritePrivate(Targets, targets.ToJsonString(Indented));
        }
        finally
        {
            Writing.Release();
        }
    }

    public async Task SetTopicAsync(string name, long? topic, CancellationToken cancellationToken)
    {
        var bot = KnownBot(name);
        await Writing.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var targets = ReadTargets();
            Topics(targets).Remove(bot);
            if (topic is { } t)
            {
                Topics(targets)[bot] = t;
            }

            WritePrivate(Targets, targets.ToJsonString(Indented));
        }
        finally
        {
            Writing.Release();
        }
    }

    public async Task<IReadOnlyList<TelegramChatView>> FindChatsAsync(string name, CancellationToken cancellationToken)
    {
        var bot = KnownBot(name);
        var token = Bots.Read(Folder, bot, needsChat: false).Token;

        // Without an offset nothing is marked as read: the listener still gets every press.
        using var form = new FormUrlEncodedContent([new("timeout", "0"), new("limit", "100")]);
        var answer = await CallAsync(token, "getUpdates", form, "Telegram did not give the bot's messages", cancellationToken).ConfigureAwait(false);
        return answer.TryGetProperty("result", out var updates) && updates.ValueKind == JsonValueKind.Array ? ChatsIn(updates) : [];
    }

    public async Task TestAsync(string name, CancellationToken cancellationToken)
    {
        var bot = KnownBot(name);
        if (ReadTargets()["chat"] is null)
        {
            throw new RejectedException("telegram-no-chat", "Choose the group first: the bot needs to know where to write.");
        }

        var (token, chat, topic) = Bots.Read(Folder, bot);
        var fields = new Dictionary<string, string> { ["chat_id"] = chat, ["text"] = $"Openspace test: {bot} writes here. Routines that use {bot} will deliver in this place." };
        if (topic is not null)
        {
            fields["message_thread_id"] = topic;
        }

        using var form = new FormUrlEncodedContent(fields);
        await CallAsync(token, "sendMessage", form, "Telegram refused the test message", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The chats seen in a bot's updates, newest first, each with the topics seen in it.</summary>
    internal static IReadOnlyList<TelegramChatView> ChatsIn(JsonElement updates)
    {
        var chats = new Dictionary<long, (string Title, string Kind, Dictionary<long, string> Topics)>();
        foreach (var update in updates.EnumerateArray().Reverse())
        {
            foreach (var part in new[] { "message", "edited_message", "channel_post", "my_chat_member", "chat_member" })
            {
                if (!update.TryGetProperty(part, out var item) || !item.TryGetProperty("chat", out var chat) || !chat.TryGetProperty("id", out var idElement)
                    || !idElement.TryGetInt64(out var id))
                {
                    continue;
                }

                if (!chats.TryGetValue(id, out var seen))
                {
                    var title = Text(chat, "title") ?? string.Join(' ', new[] { Text(chat, "first_name"), Text(chat, "last_name") }.Where(t => t is not null));
                    seen = (string.IsNullOrWhiteSpace(title) ? id.ToString(System.Globalization.CultureInfo.InvariantCulture) : title, Text(chat, "type") ?? "chat", []);
                    chats[id] = seen;
                }

                if (item.TryGetProperty("message_thread_id", out var thread) && thread.TryGetInt64(out var threadId)
                    && item.TryGetProperty("is_topic_message", out var isTopic) && isTopic.ValueKind == JsonValueKind.True)
                {
                    var topicName = TopicName(item) ?? (seen.Topics.TryGetValue(threadId, out var known) ? known : $"Topic {threadId}");
                    seen.Topics[threadId] = topicName;
                }
            }
        }

        return [.. chats.Select(c => new TelegramChatView(c.Key, c.Value.Title, c.Value.Kind, [.. c.Value.Topics.Select(t => new TelegramTopicView(t.Key, t.Value)).OrderBy(t => t.Id)]))];
    }

    private static string? TopicName(JsonElement message) =>
        message.TryGetProperty("forum_topic_created", out var created) ? Text(created, "name")
        : message.TryGetProperty("reply_to_message", out var reply) && reply.TryGetProperty("forum_topic_created", out var replyCreated) ? Text(replyCreated, "name")
        : null;

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static async Task<JsonElement> CallAsync(HttpClient http, string token, string method, HttpContent content, string failure, CancellationToken cancellationToken)
    {
        try
        {
            return await Bots.CallAsync(http, token, method, content, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            // Telegram's own words, never the address called: it holds the token.
            var reason = ex is HttpRequestException { Message: var m } && m.StartsWith("Telegram refused the message: ", StringComparison.Ordinal)
                ? m["Telegram refused the message: ".Length..]
                : "Telegram could not be reached.";
            throw new RejectedException("telegram-refused", $"{failure}: {reason}");
        }
    }

    private Task<JsonElement> CallAsync(string token, string method, HttpContent content, string failure, CancellationToken cancellationToken) =>
        CallAsync(http, token, method, content, failure, cancellationToken);

    private string KnownBot(string name)
    {
        var bot = ValidName(name);
        return File.Exists(Tokens) && TelegramOutbox.NamesIn(File.ReadLines(Tokens)).Contains(bot)
            ? bot
            : throw new RejectedException("telegram-unknown-bot", $"There is no Telegram bot called {bot}: add it first.");
    }

    internal static string ValidName(string? name)
    {
        var bot = name?.Trim().ToUpperInvariant() ?? string.Empty;
        return NamePattern().IsMatch(bot)
            ? bot
            : throw new RejectedException("telegram-name", "A bot's name in Openspace is one word of letters, digits and _, starting with a letter, at most 32: e.g. PAPER.");
    }

    private static string? NameOf(string line)
    {
        var text = line.Trim();
        var equals = text.IndexOf('=', StringComparison.Ordinal);
        return text.StartsWith('#') || equals <= 0 ? null : text[..equals].Trim().ToUpperInvariant();
    }

    private static long? NumberOf(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<long>(out var number) ? number : null;

    private JsonObject ReadTargets()
    {
        if (!File.Exists(Targets))
        {
            return [];
        }

        try
        {
            return JsonNode.Parse(File.ReadAllText(Targets)) as JsonObject ?? [];
        }
        catch (JsonException)
        {
            throw new RejectedException("telegram-file", $"{Targets} is not valid JSON: fix it or remove it, then set the group again.");
        }
    }

    private static JsonObject Topics(JsonObject targets)
    {
        if (targets["topics"] is JsonObject topics)
        {
            return topics;
        }

        var made = new JsonObject();
        targets["topics"] = made;
        return made;
    }

    private static void WritePrivate(string file, string text) => PrivateFiles.Write(file, text);

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    [GeneratedRegex("^[A-Z][A-Z0-9_]{0,31}$")]
    private static partial Regex NamePattern();

    [GeneratedRegex("^[0-9]{3,}:[A-Za-z0-9_-]{20,}$")]
    private static partial Regex TokenPattern();
}
