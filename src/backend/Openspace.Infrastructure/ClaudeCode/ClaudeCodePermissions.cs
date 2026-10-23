using System.Text.Json;

namespace Openspace.Infrastructure.ClaudeCode;

/// <summary>What Claude Code asks permission for, and the answer it expects back.</summary>
public static class ClaudeCodePermissions
{
    /// <summary>What a request asks for, to follow "Wants to": "run npm test", "edit header.php".</summary>
    public static string Describe(string tool, JsonElement input)
    {
        string? Text(string name) =>
            input.ValueKind == JsonValueKind.Object && input.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        var file = Text("file_path") ?? Text("notebook_path");
        var described = tool switch
        {
            "Bash" => $"run {Text("command") ?? "a command"}",
            "Edit" or "MultiEdit" or "NotebookEdit" => $"edit {(file is null ? "a file" : Path.GetFileName(file))}",
            "Write" => $"write {(file is null ? "a file" : Path.GetFileName(file))}",
            "WebFetch" => $"fetch {Text("url") ?? "a page"}",
            "WebSearch" => $"search the web for {Text("query") ?? "something"}",
            _ => $"use {tool}",
        };
        var line = described.ReplaceLineEndings(" ");
        return line.Length <= 200 ? line : string.Concat(line.AsSpan(0, 199), "…");
    }

    /// <summary>The answer Claude Code reads from its permission tool.</summary>
    public static string Reply(bool allow, JsonElement input) =>
        allow
            ? JsonSerializer.Serialize(new { behavior = "allow", updatedInput = input })
            : JsonSerializer.Serialize(new { behavior = "deny", message = "A person refused this in Openspace. Do something else, or explain in your summary why it was needed." });
}
