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
        var body = new Dictionary<string, object> { ["title"] = title };
        if (setup.Permissions is { } permissions)
        {
            body["permission"] = Rules(permissions);
        }

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
        // A scratch folder with nothing in it, and a session that may not touch anything.
        var folder = Path.Combine(workspaces.Value.Root, "drafts");
        Directory.CreateDirectory(folder);
        var sessionId = await CreateSessionAsync(
            folder,
            "Openspace draft",
            new AgentSetup("plan", null, null, new RolePermissions(PermissionLevel.Deny, PermissionLevel.Deny, PermissionLevel.Deny)),
            cancellationToken).ConfigureAwait(false);
        try
        {
            using var response = await http
                .PostAsJsonAsync(In($"session/{sessionId}/message", folder), Message("plan", options.Value.DraftModel, instructions, text), cancellationToken)
                .ConfigureAwait(false);
            await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
            using var reply = await JsonDocument
                .ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return string.Join(
                "\n",
                reply.RootElement.GetProperty("parts").EnumerateArray()
                    .Where(p => p.GetProperty("type").GetString() == "text")
                    .Select(p => p.GetProperty("text").GetString()));
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

    /// <summary>OpenCode permission rules for a role: one rule per capability, for every pattern.</summary>
    private static object[] Rules(RolePermissions permissions) =>
    [
        new { permission = "edit", pattern = "*", action = Action(permissions.Edit) },
        new { permission = "bash", pattern = "*", action = Action(permissions.Bash) },
        new { permission = "webfetch", pattern = "*", action = Action(permissions.Web) },
    ];

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
