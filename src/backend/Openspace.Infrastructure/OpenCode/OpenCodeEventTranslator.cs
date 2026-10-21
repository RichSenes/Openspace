using System.Text.Json;

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
            "permission.asked" => FromPermission(props),
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
            "bash" => $"Running {title ?? Text(input, "command") ?? "a command"}",
            "glob" or "grep" or "list" or "codesearch" => "Searching the code",
            "webfetch" or "websearch" => "Looking something up online",
            "task" => Text(input, "description") is { } d ? $"Delegating: {d}" : "Delegating to a helper",
            "todowrite" or "todoread" => null,
            _ => $"Using {tool}",
        };
        return phrase is null ? null : Shorten(phrase);
    }

    private static AgentSignal.Progress? FromTodos(JsonElement props)
    {
        var current = props.GetProperty("todos").EnumerateArray()
            .FirstOrDefault(t => Text(t, "status") == "in_progress");
        return current.ValueKind == JsonValueKind.Object && Text(current, "content") is { } step
            ? new AgentSignal.Progress(Text(props, "sessionID")!, Shorten(step), null)
            : null;
    }

    private static AgentSignal.Progress? FromPart(JsonElement props)
    {
        var part = props.GetProperty("part");
        if (Text(part, "type") != "tool" || !part.TryGetProperty("state", out var state) || Text(state, "status") != "running")
        {
            return null;
        }

        var input = state.TryGetProperty("input", out var i) ? i : default;
        var doing = DescribeTool(Text(part, "tool") ?? "a tool", input, Text(state, "title"));
        return doing is null ? null : new AgentSignal.Progress(Text(part, "sessionID")!, null, doing);
    }

    private static AgentSignal.PermissionAsked FromPermission(JsonElement props)
    {
        var what = Text(props, "permission") ?? "an action";
        var patterns = props.TryGetProperty("patterns", out var p) && p.ValueKind == JsonValueKind.Array
            ? string.Join(", ", p.EnumerateArray().Select(x => x.GetString()))
            : string.Empty;
        return new AgentSignal.PermissionAsked(
            Text(props, "sessionID")!,
            Text(props, "id")!,
            Shorten(patterns.Length > 0 ? $"{what} {patterns}" : what));
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
