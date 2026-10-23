using System.Text.Json;
using Openspace.Application.Agents;
using Openspace.Domain.Work;

namespace Openspace.Infrastructure.OpenCode;

/// <summary>
/// Turns raw OpenCode events into signals, and tool calls into short human phrases
/// ("Editing header.php") for speech bubbles.
/// </summary>
internal static class OpenCodeEventTranslator
{
    private const int MaxPhrase = 80;

    /// <param name="payload">An event as found in the <c>payload</c> of <c>/global/event</c>.</param>
    public static AgentSignal? Translate(JsonElement payload)
    {
        if (!payload.TryGetProperty("type", out var typeElement) || !payload.TryGetProperty("properties", out var props))
        {
            return null;
        }

        return typeElement.GetString() switch
        {
            "todo.updated" => FromTodos(props),
            "message.part.updated" => FromPart(props),
            "message.updated" => FromMessage(props),
            "permission.asked" => FromPermission(props),
            "permission.replied" => Text(props, "sessionID") is { } replied && Text(props, "requestID") is { } request
                ? new AgentSignal.PermissionReplied(replied, request, Text(props, "reply") is not "reject")
                : null,
            "session.idle" => new AgentSignal.Finished(Text(props, "sessionID")!),
            "session.error" => FromError(props),
            _ => null,
        };
    }

    public static string? DescribeTool(string tool, JsonElement input, string? title)
    {
        string? File(string name) => Text(input, name) is { } path ? Path.GetFileName(path) : null;

        var phrase = tool switch
        {
            "read" => File("filePath") is { } f ? $"Reading {f}" : "Reading a file",
            "edit" or "multiedit" or "patch" => File("filePath") is { } f ? $"Editing {f}" : "Editing code",
            "write" => File("filePath") is { } f ? $"Writing {f}" : "Writing a file",
            "bash" => ActionPhrases.Doing(new AgentAction(ActionKind.Run, Text(input, "command") ?? title ?? string.Empty)),
            "glob" or "grep" or "list" or "codesearch" => "Searching the code",
            "webfetch" or "websearch" => "Looking something up online",
            "task" => Text(input, "description") is { } d ? $"Delegating: {d}" : "Delegating to a helper",
            "todowrite" or "todoread" => null,
            _ => $"Using {tool}",
        };
        return phrase is null ? null : Shorten(phrase);
    }

    /// <summary>An answer of the agent is complete: what it spent, reasoning counted as output.</summary>
    private static AgentSignal.Used? FromMessage(JsonElement props)
    {
        if (!props.TryGetProperty("info", out var info) || Text(info, "role") != "assistant"
            || !info.TryGetProperty("time", out var time) || !time.TryGetProperty("completed", out var completed) || completed.ValueKind != JsonValueKind.Number
            || !info.TryGetProperty("tokens", out var tokens) || Text(info, "sessionID") is not { } sessionId)
        {
            return null;
        }

        static long Count(JsonElement element, string name) =>
            element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var n) && n.ValueKind == JsonValueKind.Number ? n.GetInt64() : 0;
        var cache = tokens.TryGetProperty("cache", out var c) ? c : default;
        return new AgentSignal.Used(
            sessionId,
            new TokenCount(Count(tokens, "input"), Count(tokens, "output") + Count(tokens, "reasoning"), Count(cache, "read"), Count(cache, "write")),
            Text(info, "id"));
    }

    private static AgentSignal.Progress? FromTodos(JsonElement props)
    {
        var current = props.GetProperty("todos").EnumerateArray()
            .FirstOrDefault(t => Text(t, "status") == "in_progress");
        return current.ValueKind == JsonValueKind.Object && Text(current, "content") is { } step
            ? new AgentSignal.Progress(Text(props, "sessionID")!, Shorten(step), null)
            : null;
    }

    private static AgentSignal? FromPart(JsonElement props)
    {
        var part = props.GetProperty("part");
        if (Text(part, "type") != "tool" || !part.TryGetProperty("state", out var state))
        {
            return null;
        }

        var tool = Text(part, "tool") ?? "a tool";

        // Calls to Openspace's own tools are logged through what they do (a note, a step).
        if (tool.StartsWith("openspace_", StringComparison.Ordinal))
        {
            return null;
        }

        var input = state.TryGetProperty("input", out var i) ? i : default;
        var sessionId = Text(part, "sessionID")!;
        switch (Text(state, "status"))
        {
            case "running":
                var doing = DescribeTool(tool, input, Text(state, "title"));
                return doing is null ? null : new AgentSignal.Progress(sessionId, null, doing);
            case "completed" or "error" when Text(part, "callID") is { } callId && tool is not ("todowrite" or "todoread"):
                return Finished(sessionId, callId, tool, input, state);
            default:
                return null;
        }
    }

    /// <summary>What a finished tool call leaves in the audit log: a past-tense summary and its evidence.</summary>
    private static AgentSignal.ToolFinished Finished(string sessionId, string callId, string tool, JsonElement input, JsonElement state)
    {
        var failed = Text(state, "status") == "error";
        var metadata = state.TryGetProperty("metadata", out var m) ? m : default;
        var output = Text(state, "output");
        string? File(string name) => Text(input, name);
        string Name(string? path) => path is null ? "a file" : Path.GetFileName(path);

        var (kind, summary, detail) = tool switch
        {
            "read" => (AuditKind.Read, $"Read {Name(File("filePath"))}", File("filePath")),
            "edit" or "multiedit" or "patch" => (AuditKind.Change, $"Changed {Name(File("filePath"))}", Text(metadata, "diff") ?? File("filePath")),
            "write" => (AuditKind.Change, $"Wrote {Name(File("filePath"))}", Text(metadata, "diff") ?? File("filePath")),
            "bash" => (AuditKind.Command, ActionPhrases.Done(new AgentAction(ActionKind.Run, Text(input, "command") ?? Text(state, "title") ?? "a command")), $"$ {Text(input, "command")}\n{output}"),
            "glob" or "grep" or "list" or "codesearch" =>
                (AuditKind.Search, Text(input, "pattern") is { } pattern ? $"Searched the code for {pattern}" : "Searched the code", output),
            "webfetch" or "websearch" => (AuditKind.Other, $"Looked up {Text(input, "url") ?? Text(input, "query") ?? "something online"}", null),
            _ => (AuditKind.Other, $"Used {tool}", output),
        };

        if (tool == "bash" && metadata.ValueKind == JsonValueKind.Object
            && metadata.TryGetProperty("exit", out var exit) && exit.ValueKind == JsonValueKind.Number && exit.GetInt32() != 0)
        {
            failed = true;
        }

        if (failed && Text(state, "error") is { } error)
        {
            detail = detail is null ? error : $"{detail}\n{error}";
        }

        return new AgentSignal.ToolFinished(sessionId, callId, kind, Shorten(summary), detail, !failed);
    }

    private static AgentSignal.PermissionAsked FromPermission(JsonElement props) =>
        new(Text(props, "sessionID")!, Text(props, "id")!, ActionOf(props));

    /// <summary>What a permission request asks for: to run "ls -la &amp;&amp; git status", to edit header.php.</summary>
    public static AgentAction ActionOf(JsonElement request)
    {
        var what = Text(request, "permission") ?? "an action";
        var patterns = request.TryGetProperty("patterns", out var p) && p.ValueKind == JsonValueKind.Array
            ? [.. p.EnumerateArray().Select(x => x.GetString()).OfType<string>()]
            : new List<string>();
        var metadata = request.TryGetProperty("metadata", out var m) ? m : default;
        return what switch
        {
            "bash" => new AgentAction(ActionKind.Run, patterns.Count > 0 ? string.Join(" && ", patterns) : Text(metadata, "command") ?? "a command"),
            "edit" => new AgentAction(ActionKind.Edit, Text(metadata, "filepath") ?? patterns.FirstOrDefault() ?? "a file"),
            "external_directory" => new AgentAction(ActionKind.OutsideFolder, patterns.Count > 0 ? string.Join(", ", patterns) : "another folder"),
            "webfetch" or "websearch" => new AgentAction(ActionKind.Fetch, patterns.FirstOrDefault() ?? Text(metadata, "url") ?? "a page"),
            _ => new AgentAction(ActionKind.Other, what),
        };
    }

    private static AgentSignal.Failed? FromError(JsonElement props)
    {
        var sessionId = Text(props, "sessionID");
        if (sessionId is null)
        {
            return null;
        }

        var error = props.TryGetProperty("error", out var e) ? e : default;
        var name = Text(error, "name");
        if (name == "MessageAbortedError")
        {
            return null;
        }

        var message = error.ValueKind == JsonValueKind.Object && error.TryGetProperty("data", out var data)
            ? Text(data, "message")
            : null;
        return new AgentSignal.Failed(sessionId, $"The agent stopped with an error: {message ?? name ?? "unknown error"}");
    }

    private static string? Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string Shorten(string text)
    {
        var line = text.ReplaceLineEndings(" ").Trim();
        return line.Length <= MaxPhrase ? line : string.Concat(line.AsSpan(0, MaxPhrase - 1), "…");
    }
}
