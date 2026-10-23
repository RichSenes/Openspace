using System.Text.Json;
using Openspace.Application.Agents;
using Openspace.Domain.Work;
using Openspace.Infrastructure.OpenCode;

namespace Openspace.Infrastructure.ClaudeCode;

/// <summary>
/// Reads the stream of one Claude Code session (<c>--output-format stream-json</c>), line by
/// line, into agent signals. It remembers each tool call until its result arrives, the
/// agent's last words and how much of the account's limits is used.
/// </summary>
internal sealed class ClaudeCodeEvents(string sessionId)
{
    private readonly Dictionary<string, (string Name, JsonElement Input)> _calls = [];

    /// <summary>The text of the agent's last answer, usually its summary of the work.</summary>
    public string? LastReply { get; private set; }

    /// <summary>The run ended with an answer, failed or not.</summary>
    public bool Ended { get; private set; }

    public IReadOnlyList<AgentSignal> Read(string line, ClaudeCodeUsage? usage = null)
    {
        if (string.IsNullOrWhiteSpace(line) || line[0] != '{')
        {
            return [];
        }

        using var json = JsonDocument.Parse(line);
        var root = json.RootElement;
        switch (Text(root, "type"))
        {
            case "assistant":
                return FromAssistant(root);
            case "user":
                return FromResults(root);
            case "rate_limit_event":
                usage?.Update(root);
                return [];
            case "result":
                Ended = true;
                var spent = Tokens(root);
                var result = Text(root, "result");
                if (root.TryGetProperty("is_error", out var error) && error.ValueKind == JsonValueKind.True)
                {
                    return [.. spent, new AgentSignal.Failed(sessionId, $"Claude Code stopped: {Shorten(result ?? Text(root, "subtype") ?? "unknown error", 300)}")];
                }

                if (!string.IsNullOrWhiteSpace(result))
                {
                    LastReply = result.Trim();
                }

                return [.. spent, new AgentSignal.Finished(sessionId)];
            default:
                return [];
        }
    }

    /// <summary>What the whole run spent, as the result reports it.</summary>
    private AgentSignal[] Tokens(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        long Count(string name) => usage.TryGetProperty(name, out var n) && n.ValueKind == JsonValueKind.Number ? n.GetInt64() : 0;
        return [new AgentSignal.Used(sessionId, new TokenCount(
            Count("input_tokens"), Count("output_tokens"), Count("cache_read_input_tokens"), Count("cache_creation_input_tokens")))];
    }

    private List<AgentSignal> FromAssistant(JsonElement root)
    {
        var signals = new List<AgentSignal>();
        foreach (var part in Content(root))
        {
            var kind = Text(part, "type");
            if (kind == "text" && Text(part, "text") is { Length: > 0 } text)
            {
                LastReply = text.Trim();
            }

            if (kind != "tool_use" || Text(part, "id") is not { } id || Text(part, "name") is not { } name)
            {
                continue;
            }

            var input = part.TryGetProperty("input", out var i) ? i.Clone() : default;
            _calls[id] = (name, input);
            if (name == "TodoWrite")
            {
                if (CurrentTodo(input) is { } step)
                {
                    signals.Add(new AgentSignal.Progress(sessionId, Shorten(step, 120), null));
                }
            }
            else if (!name.StartsWith("mcp__openspace__", StringComparison.Ordinal))
            {
                signals.Add(new AgentSignal.Progress(sessionId, null, Shorten(Doing(name, input), 120)));
            }
        }

        return signals;
    }

    private List<AgentSignal> FromResults(JsonElement root)
    {
        var signals = new List<AgentSignal>();
        foreach (var part in Content(root))
        {
            if (Text(part, "type") != "tool_result" || Text(part, "tool_use_id") is not { } id || !_calls.Remove(id, out var call))
            {
                continue;
            }

            if (call.Name is "TodoWrite" || call.Name.StartsWith("mcp__", StringComparison.Ordinal))
            {
                continue;
            }

            var failed = part.TryGetProperty("is_error", out var e) && e.ValueKind == JsonValueKind.True;
            signals.Add(Finished(id, call.Name, call.Input, ResultText(part), failed));
        }

        return signals;
    }

    /// <summary>What a finished tool call leaves in the audit log, as OpenCode's do.</summary>
    private AgentSignal.ToolFinished Finished(string callId, string tool, JsonElement input, string? output, bool failed)
    {
        string? File() => Text(input, "file_path") ?? Text(input, "notebook_path");
        string Name(string? path) => path is null ? "a file" : Path.GetFileName(path);
        var (kind, summary, detail) = tool switch
        {
            "Read" => (AuditKind.Read, $"Read {Name(File())}", File()),
            "Edit" or "MultiEdit" or "NotebookEdit" => (AuditKind.Change, $"Changed {Name(File())}", EditDetail(input) ?? File()),
            "Write" => (AuditKind.Change, $"Wrote {Name(File())}", File()),
            "Bash" => (AuditKind.Command, ActionPhrases.Done(new AgentAction(ActionKind.Run, Text(input, "command") ?? "a command")), $"$ {Text(input, "command")}\n{output}"),
            "Grep" or "Glob" =>
                (AuditKind.Search, Text(input, "pattern") is { } pattern ? $"Searched the code for {pattern}" : "Searched the code", output),
            "WebFetch" or "WebSearch" => (AuditKind.Other, $"Looked up {Text(input, "url") ?? Text(input, "query") ?? "something online"}", null),
            _ => (AuditKind.Other, $"Used {tool}", output),
        };

        if (failed && output is not null && kind is not AuditKind.Command)
        {
            detail = detail is null ? output : $"{detail}\n{output}";
        }

        return new AgentSignal.ToolFinished(sessionId, callId, kind, Shorten(summary, 160), detail, !failed);
    }

    /// <summary>The replaced and new text of an edit, as a small diff.</summary>
    private static string? EditDetail(JsonElement input)
    {
        var before = Text(input, "old_string");
        var after = Text(input, "new_string");
        if (before is null && after is null)
        {
            return null;
        }

        static IEnumerable<string> Lines(string? text, char sign) =>
            (text ?? string.Empty).Split('\n').Select(l => $"{sign}{l}");
        return $"--- {Text(input, "file_path")}\n{string.Join('\n', Lines(before, '-'))}\n{string.Join('\n', Lines(after, '+'))}";
    }

    /// <summary>What the agent is doing, to show in its bubble: "Editing header.php", "Running the tests".</summary>
    internal static string Doing(string tool, JsonElement input)
    {
        var file = Text(input, "file_path") ?? Text(input, "notebook_path");
        var name = file is null ? null : Path.GetFileName(file);
        return tool switch
        {
            "Read" => $"Reading {name ?? "a file"}",
            "Edit" or "MultiEdit" or "NotebookEdit" => $"Editing {name ?? "a file"}",
            "Write" => $"Writing {name ?? "a file"}",
            "Bash" => ActionPhrases.Doing(new AgentAction(ActionKind.Run, Text(input, "command") ?? string.Empty)),
            "Grep" or "Glob" => "Searching the code",
            "WebFetch" or "WebSearch" => "Looking something up online",
            _ => $"Using {tool}",
        };
    }

    /// <summary>The item of the agent's todo list it is working on now.</summary>
    private static string? CurrentTodo(JsonElement input) =>
        input.ValueKind == JsonValueKind.Object && input.TryGetProperty("todos", out var todos) && todos.ValueKind == JsonValueKind.Array
            ? todos.EnumerateArray().Where(t => Text(t, "status") == "in_progress").Select(t => Text(t, "activeForm") ?? Text(t, "content")).FirstOrDefault()
            : null;

    private static JsonElement[] Content(JsonElement root) =>
        root.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array
            ? [.. content.EnumerateArray()]
            : [];

    private static string? ResultText(JsonElement part)
    {
        if (!part.TryGetProperty("content", out var content))
        {
            return null;
        }

        return content.ValueKind switch
        {
            JsonValueKind.String => content.GetString(),
            JsonValueKind.Array => string.Join("\n", content.EnumerateArray().Select(c => Text(c, "text")).Where(t => t is not null)),
            _ => null,
        };
    }

    private static string Shorten(string text, int max)
    {
        var line = text.ReplaceLineEndings(" ").Trim();
        return line.Length <= max ? line : string.Concat(line.AsSpan(0, max - 1), "…");
    }

    private static string? Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
