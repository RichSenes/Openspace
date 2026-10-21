using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Openspace.Application.Abstractions;
using Openspace.Domain.Roles;

namespace Openspace.Infrastructure.OpenCode;

/// <summary>
/// Talks to <c>opencode serve</c> over its HTTP API. Every call names the folder it
/// is about, so one server serves every task's working copy.
/// </summary>
internal sealed class OpenCodeRuntime(
    HttpClient http, IOptions<OpenCodeOptions> options, IOptions<Git.WorkspaceOptions> workspaces) : IAgentRuntime
{
    public async Task<bool> HasAgentAsync(string agentName, string workspacePath, CancellationToken cancellationToken)
    {
        using var agents = await GetJsonAsync($"agent?directory={Uri.EscapeDataString(workspacePath)}", cancellationToken)
            .ConfigureAwait(false);
        return agents.RootElement.EnumerateArray().Any(a => a.GetProperty("name").GetString() == agentName);
    }

    public async Task<string> CreateSessionAsync(
        string workspacePath, string title, AgentSetup setup, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(setup);
        var body = new Dictionary<string, object> { ["title"] = title, ["permission"] = Rules(setup.Permissions) };

        using var response = await http
            .PostAsJsonAsync(In("session", workspacePath), body, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        using var session = await JsonDocument
            .ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return session.RootElement.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("OpenCode created a session without an id.");
    }

    public async Task PromptAsync(
        string sessionId, string workspacePath, AgentSetup setup, string text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(setup);
        var body = Message(setup.AgentName, setup.Model, setup.Instructions, text);
        using var response = await http
            .PostAsJsonAsync(In($"session/{sessionId}/prompt_async", workspacePath), body, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> DraftAsync(string instructions, string text, CancellationToken cancellationToken)
    {
        // A scratch folder with nothing in it.
        var folder = Path.Combine(workspaces.Value.Root, "drafts");
        Directory.CreateDirectory(folder);
        using var reply = await AskReadOnlyAsync(folder, "Openspace draft", instructions, text, null, cancellationToken).ConfigureAwait(false);
        return string.Join(
            "\n",
            reply.RootElement.GetProperty("parts").EnumerateArray()
                .Where(p => p.GetProperty("type").GetString() == "text")
                .Select(p => p.GetProperty("text").GetString()));
    }

    public async Task<string> PlanAsync(
        string folder, string instructions, string text, string jsonSchema, CancellationToken cancellationToken)
    {
        using var schema = JsonDocument.Parse(jsonSchema);
        var format = new { type = "json_schema", schema = schema.RootElement, retryCount = 2 };
        using var reply = await AskReadOnlyAsync(folder, "Openspace plan", instructions, text, format, cancellationToken).ConfigureAwait(false);
        var info = reply.RootElement.GetProperty("info");
        if (info.TryGetProperty("structured", out var structured) && structured.ValueKind == JsonValueKind.Object)
        {
            return structured.GetRawText();
        }

        var error = info.TryGetProperty("error", out var e) && e.TryGetProperty("data", out var data) && data.TryGetProperty("message", out var m)
            ? m.GetString()
            : "no structured answer";
        throw new InvalidOperationException($"The model did not return a plan ({error}).");
    }

    /// <summary>
    /// One question, one answer, in a session that may read but not edit, run commands
    /// or use the web. The session is deleted afterwards.
    /// </summary>
    private async Task<JsonDocument> AskReadOnlyAsync(
        string folder, string title, string instructions, string text, object? format, CancellationToken cancellationToken)
    {
        var sessionId = await CreateSessionAsync(
            folder,
            title,
            new AgentSetup("plan", null, null, new RolePermissions(PermissionLevel.Deny, PermissionLevel.Deny, PermissionLevel.Deny)),
            cancellationToken).ConfigureAwait(false);
        try
        {
            var body = Message("plan", options.Value.DraftModel, instructions, text);
            if (format is not null)
            {
                body["format"] = format;
            }

            using var response = await http
                .PostAsJsonAsync(In($"session/{sessionId}/message", folder), body, cancellationToken)
                .ConfigureAwait(false);
            await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
            return await JsonDocument
                .ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            using var _ = await http.DeleteAsync(In($"session/{sessionId}", folder), CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>The header that tells the Openspace tools which task a call belongs to.</summary>
    public const string TaskHeader = "X-Openspace-Task";

    public async Task ConnectToolsAsync(string workspacePath, Guid taskId, CancellationToken cancellationToken)
    {
        var body = new
        {
            name = "openspace",
            config = new Dictionary<string, object>
            {
                ["type"] = "remote",
                ["url"] = options.Value.ToolsUrl.ToString(),
                ["enabled"] = true,
                ["oauth"] = false,
                ["headers"] = new Dictionary<string, string> { [TaskHeader] = taskId.ToString() },
            },
        };
        using var response = await http.PostAsJsonAsync(In("mcp", workspacePath), body, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task AbortAsync(string sessionId, string workspacePath, CancellationToken cancellationToken)
    {
        using var response = await http
            .PostAsync(In($"session/{sessionId}/abort", workspacePath), null, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string?> GetLastReplyAsync(string sessionId, string workspacePath, CancellationToken cancellationToken)
    {
        using var messages = await GetJsonAsync(In($"session/{sessionId}/message", workspacePath), cancellationToken)
            .ConfigureAwait(false);
        var texts = messages.RootElement.EnumerateArray()
            .Where(m => m.GetProperty("info").GetProperty("role").GetString() == "assistant")
            .Select(m => string.Join(
                "\n",
                m.GetProperty("parts").EnumerateArray()
                    .Where(p => p.GetProperty("type").GetString() == "text")
                    .Select(p => p.GetProperty("text").GetString()?.Trim())
                    .Where(t => !string.IsNullOrEmpty(t))))
            .Where(t => t.Length > 0)
            .ToList();
        return texts.Count > 0 ? texts[^1] : null;
    }

    public async Task<IReadOnlyList<PermissionRequest>> PendingPermissionsAsync(string workspacePath, CancellationToken cancellationToken)
    {
        using var pending = await GetJsonAsync(In("permission", workspacePath), cancellationToken).ConfigureAwait(false);
        return [.. pending.RootElement.EnumerateArray().Select(p => new PermissionRequest(
            p.GetProperty("id").GetString()!,
            p.GetProperty("sessionID").GetString()!,
            OpenCodeEventTranslator.DescribePermission(p)))];
    }

    public async Task ReplyToPermissionAsync(
        string permissionId, string workspacePath, bool allow, CancellationToken cancellationToken)
    {
        using var response = await http
            .PostAsJsonAsync(In($"permission/{permissionId}/reply", workspacePath), new { reply = allow ? "once" : "reject" }, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private static Dictionary<string, object> Message(string agent, string? model, string? instructions, string text)
    {
        var body = new Dictionary<string, object>
        {
            ["agent"] = agent,
            ["parts"] = new[] { new { type = "text", text } },
        };
        if (model is not null)
        {
            var slash = model.IndexOf('/', StringComparison.Ordinal);
            body["model"] = new { providerID = model[..slash], modelID = model[(slash + 1)..] };
        }

        if (instructions is not null)
        {
            body["system"] = instructions;
        }

        return body;
    }

    /// <summary>
    /// OpenCode permission rules: one per role capability, for every pattern, plus a rule
    /// that always keeps the agent inside its own folder unless a person agrees.
    /// Read-only sessions (all denied) may not leave it at all.
    /// </summary>
    private static object[] Rules(RolePermissions? permissions)
    {
        var outside = permissions is { Edit: PermissionLevel.Deny, Bash: PermissionLevel.Deny, Web: PermissionLevel.Deny } ? "deny" : "ask";
        var rules = new List<object> { new { permission = "external_directory", pattern = "*", action = outside } };
        if (permissions is not null)
        {
            rules.Add(new { permission = "edit", pattern = "*", action = Action(permissions.Edit) });
            rules.Add(new { permission = "bash", pattern = "*", action = Action(permissions.Bash) });
            rules.Add(new { permission = "webfetch", pattern = "*", action = Action(permissions.Web) });
        }

        return [.. rules];
    }

    private static string Action(PermissionLevel level) => level switch
    {
        PermissionLevel.Allow => "allow",
        PermissionLevel.Ask => "ask",
        _ => "deny",
    };

    private static string In(string path, string workspacePath) => $"{path}?directory={Uri.EscapeDataString(workspacePath)}";

    private async Task<JsonDocument> GetJsonAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(path, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await JsonDocument
            .ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new HttpRequestException(
                $"OpenCode answered {(int)response.StatusCode} to {response.RequestMessage?.RequestUri?.AbsolutePath}: {detail}",
                null,
                response.StatusCode);
        }
    }
}
