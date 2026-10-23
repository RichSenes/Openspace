using System.Text.Json;
using Openspace.Application.Agents;

namespace Openspace.Infrastructure.ClaudeCode;

/// <summary>What Claude Code asks permission for, and the answer it expects back.</summary>
public static class ClaudeCodePermissions
{
    /// <summary>What a request asks for: to run "npm test", to edit header.php.</summary>
    public static AgentAction Action(string tool, JsonElement input)
    {
        string? Text(string name) =>
            input.ValueKind == JsonValueKind.Object && input.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        var file = Text("file_path") ?? Text("notebook_path") ?? "a file";
        return tool switch
        {
            "Bash" => new AgentAction(ActionKind.Run, Text("command") ?? "a command"),
            "Edit" or "MultiEdit" or "NotebookEdit" => new AgentAction(ActionKind.Edit, file),
            "Write" => new AgentAction(ActionKind.Write, file),
            "WebFetch" => new AgentAction(ActionKind.Fetch, Text("url") ?? "a page"),
            "WebSearch" => new AgentAction(ActionKind.Fetch, Text("query") ?? "something"),
            _ => new AgentAction(ActionKind.Other, tool),
        };
    }

    /// <summary>The answer Claude Code reads from its permission tool.</summary>
    public static string Reply(bool allow, JsonElement input) =>
        allow
            ? JsonSerializer.Serialize(new { behavior = "allow", updatedInput = input })
            : JsonSerializer.Serialize(new { behavior = "deny", message = "A person refused this in Openspace. Do something else, or explain in your summary why it was needed." });
}
