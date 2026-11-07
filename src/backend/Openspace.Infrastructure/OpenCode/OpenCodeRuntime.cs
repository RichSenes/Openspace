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
    HttpClient http, IOptionsMonitor<OpenCodeOptions> options, IOptions<Git.WorkspaceOptions> workspaces, Skills.SkillCatalog? skills = null)
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
        var body = new Dictionary<string, object> { ["title"] = title, ["permission"] = Rules(setup.Permissions, setup.PrivatePaths ?? [], skills?.ReadFolders ?? [], setup.Skills, setup.OutputFolder) };

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
        string folder, string instructions, string text, string jsonSchema, IReadOnlyList<string> privatePaths, CancellationToken cancellationToken)
    {
        using var schema = JsonDocument.Parse(jsonSchema);
        var format = new { type = "json_schema", schema = schema.RootElement, retryCount = 2 };
        using var reply = await AskReadOnlyAsync(folder, "Openspace plan", instructions, text, format, cancellationToken, privatePaths).ConfigureAwait(false);
        return Structured(reply, "plan");
    }

    /// <summary>
    /// A review of finished work by <see cref="OpenCodeOptions.ReviewModel"/>, in a session that
    /// may read the working copy but runs nothing: the changes travel with the question, as
    /// git shows them. Null when no review model is set.
    /// </summary>
    public async Task<string?> ReviewAsync(
        string folder, string instructions, string text, string jsonSchema, IReadOnlyList<string> privatePaths, CancellationToken cancellationToken)
    {
        if (options.CurrentValue.ReviewModel is not { Length: > 0 } model)
        {
            return null;
        }

        var changes = await ChangesAsync(folder, cancellationToken).ConfigureAwait(false);
        var question = $"{text}\n\nYou cannot run commands here: these are the changes, as git diff HEAD shows them, new files included.\n\n```diff\n{changes}\n```";
        using var schema = JsonDocument.Parse(jsonSchema);
        var format = new { type = "json_schema", schema = schema.RootElement, retryCount = 2 };
        using var reply = await AskReadOnlyAsync(folder, "Openspace review", instructions, question, format, cancellationToken, privatePaths, model).ConfigureAwait(false);
        return Structured(reply, "review");
    }

    /// <summary>Most of a diff a review is sent: enough for a task's work, never a whole repository.</summary>
    internal const int MostChanges = 60_000;

    /// <summary>What the agent changed in its working copy, new files included, cut when very long.</summary>
    private static async Task<string> ChangesAsync(string folder, CancellationToken cancellationToken)
    {
        await Git.GitCli.RunAsync(folder, cancellationToken, "add", "--intent-to-add", "--all").ConfigureAwait(false);
        var diff = await Git.GitCli.RunAsync(folder, cancellationToken, "diff", "HEAD").ConfigureAwait(false);
        return diff.Length <= MostChanges ? diff : $"{diff[..MostChanges]}\n[… the rest of the diff is left out: read the files you need]";
    }

    private static string Structured(JsonDocument reply, string what)
    {
        var info = reply.RootElement.GetProperty("info");
        if (info.TryGetProperty("structured", out var structured) && structured.ValueKind == JsonValueKind.Object)
        {
            return structured.GetRawText();
        }

        var error = info.TryGetProperty("error", out var e) && e.TryGetProperty("data", out var data) && data.TryGetProperty("message", out var m)
            ? m.GetString()
            : "no structured answer";
        throw new InvalidOperationException($"The model did not return a {what} ({error}).");
    }

    /// <summary>
    /// One question, one answer, in a session that may read but not edit, run commands
    /// or use the web. The session is deleted afterwards.
    /// </summary>
    private async Task<JsonDocument> AskReadOnlyAsync(
        string folder, string title, string instructions, string text, object? format, CancellationToken cancellationToken,
        IReadOnlyList<string>? privatePaths = null, string? model = null)
    {
        var sessionId = await CreateSessionAsync(
            folder,
            title,
            new AgentSetup("plan", null, null, new RolePermissions(PermissionLevel.Deny, PermissionLevel.Deny, PermissionLevel.Deny), PrivatePaths: privatePaths),
            cancellationToken).ConfigureAwait(false);
        try
        {
            model ??= options.CurrentValue.DraftModel;
            var body = Message("plan", string.IsNullOrWhiteSpace(model) ? null : model, instructions, text);
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

    /// <summary>Every model OpenCode can use, as <c>provider/model</c>, its default first.</summary>
    public async Task<IReadOnlyList<ModelOption>> ModelsAsync(CancellationToken cancellationToken)
    {
        using var config = await GetJsonAsync("config/providers", cancellationToken).ConfigureAwait(false);
        var root = config.RootElement;
        var defaults = root.TryGetProperty("default", out var d) && d.ValueKind == JsonValueKind.Object
            ? d.EnumerateObject().Select(p => $"{p.Name}/{p.Value.GetString()}").ToHashSet()
            : [];
        var models = new List<ModelOption>();
        foreach (var provider in root.GetProperty("providers").EnumerateArray())
        {
            var providerId = provider.GetProperty("id").GetString()!;
            var providerName = provider.TryGetProperty("name", out var pn) ? pn.GetString() : providerId;
            if (!provider.TryGetProperty("models", out var list) || list.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (var model in list.EnumerateObject())
            {
                var name = model.Value.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()! : model.Name;
                models.Add(new ModelOption($"{providerId}/{model.Name}", $"{name} · {providerName}"));
            }
        }

        return [.. models.OrderBy(m => defaults.Contains(m.Id) ? 0 : 1).ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>The most each model holds in its context, by <c>provider/model</c>, for the models that say.</summary>
    public async Task<IReadOnlyDictionary<string, long>> ContextWindowsAsync(CancellationToken cancellationToken)
    {
        using var config = await GetJsonAsync("config/providers", cancellationToken).ConfigureAwait(false);
        var windows = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var provider in config.RootElement.GetProperty("providers").EnumerateArray())
        {
            if (provider.GetProperty("id").GetString() is not { } providerId
                || !provider.TryGetProperty("models", out var list) || list.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (var model in list.EnumerateObject())
            {
                if (model.Value.ValueKind == JsonValueKind.Object
                    && model.Value.TryGetProperty("limit", out var limit) && limit.ValueKind == JsonValueKind.Object
                    && limit.TryGetProperty("context", out var context) && context.ValueKind == JsonValueKind.Number && context.GetInt64() > 0)
                {
                    windows[$"{providerId}/{model.Name}"] = context.GetInt64();
                }
            }
        }

        return windows;
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
                ["url"] = options.CurrentValue.ToolsUrl.ToString(),
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

    /// <summary>
    /// The session a helper's session hangs from, up to the agent's own: helpers run in
    /// sessions of their own, and Openspace only knows the agent's.
    /// </summary>
    public async Task<string> RootSessionAsync(string sessionId, string? directory, CancellationToken cancellationToken)
    {
        var current = sessionId;
        for (var depth = 0; depth < 8; depth++)
        {
            var path = directory is null ? $"session/{current}" : In($"session/{current}", directory);
            using var session = await GetJsonAsync(path, cancellationToken).ConfigureAwait(false);
            if (!session.RootElement.TryGetProperty("parentID", out var parent) || parent.GetString() is not { Length: > 0 } next)
            {
                return current;
            }

            current = next;
        }

        return current;
    }

    public async Task<IReadOnlyList<PermissionRequest>> PendingPermissionsAsync(string workspacePath, CancellationToken cancellationToken)
    {
        using var pending = await GetJsonAsync(In("permission", workspacePath), cancellationToken).ConfigureAwait(false);
        return [.. pending.RootElement.EnumerateArray().Select(p => new PermissionRequest(
            p.GetProperty("id").GetString()!,
            p.GetProperty("sessionID").GetString()!,
            OpenCodeEventTranslator.ActionOf(p)))];
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
    /// <summary>What an agent may touch, in OpenCode's terms. Later rules win, so private folders come last.</summary>
    /// <param name="skillFolders">Where the installed skills are read from: readable by every agent, never writable.</param>
    /// <param name="skills">The skills the agent may load; null for all of them.</param>
    /// <param name="outputFolder">A folder outside its copy the agent may also write in.</param>
    internal static object[] Rules(
        RolePermissions? permissions,
        IReadOnlyList<string> privatePaths,
        IReadOnlyList<string>? skillFolders = null,
        IReadOnlyList<string>? skills = null,
        string? outputFolder = null)
    {
        var outside = permissions is { Edit: PermissionLevel.Deny, Bash: PermissionLevel.Deny, Web: PermissionLevel.Deny } ? "deny" : "ask";
        var rules = new List<object> { new { permission = "external_directory", pattern = "*", action = outside } };
        if (permissions is not null)
        {
            rules.Add(new { permission = "edit", pattern = "*", action = Action(permissions.Edit) });
            rules.Add(new { permission = "bash", pattern = "*", action = Action(permissions.Bash) });
            rules.Add(new { permission = "webfetch", pattern = "*", action = Action(permissions.Web) });
        }

        if (skills is not null)
        {
            rules.Add(new { permission = "skill", pattern = "*", action = "deny" });
            rules.AddRange(skills.Select(name => (object)new { permission = "skill", pattern = name, action = "allow" }));
        }

        foreach (var folder in skillFolders ?? [])
        {
            // A loaded skill's other files are read from its folder, outside the copy.
            rules.Add(new { permission = "external_directory", pattern = $"{folder}/*", action = "allow" });
            rules.Add(new { permission = "edit", pattern = $"{folder}/*", action = "deny" });
        }

        if (outputFolder is not null)
        {
            rules.Add(new { permission = "external_directory", pattern = $"{outputFolder}/*", action = "allow" });
        }

        foreach (var folder in privatePaths)
        {
            foreach (var permission in PathPermissions)
            {
                rules.Add(new { permission, pattern = folder, action = "deny" });
                rules.Add(new { permission, pattern = $"{folder}/*", action = "deny" });
            }

            rules.Add(new { permission = "bash", pattern = $"*{folder}*", action = "deny" });
        }

        return [.. rules];
    }

    /// <summary>OpenCode permissions that name a path, all refused for private folders.</summary>
    private static readonly string[] PathPermissions = ["external_directory", "read", "edit", "glob", "grep", "list"];

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
