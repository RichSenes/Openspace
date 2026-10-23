using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Openspace.Application.Abstractions;
using Openspace.Domain.Work;

namespace Openspace.Api.Tests;

/// <summary>Talks to the real /mcp endpoint with the official MCP client, as OpenCode would.</summary>
public sealed class McpTests : IAsyncDisposable
{
    private readonly OpenspaceApp _app = new();

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    private async Task<McpClient> ConnectAsync(Guid? taskId)
    {
        var headers = new Dictionary<string, string>();
        if (taskId is { } id)
        {
            headers["X-Openspace-Task"] = id.ToString();
        }

        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(_app.Server.BaseAddress, "mcp"),
                AdditionalHeaders = headers,
                TransportMode = HttpTransportMode.StreamableHttp,
            },
            _app.CreateClient(),
            ownsHttpClient: true);
        return await McpClient.CreateAsync(transport);
    }

    /// <summary>A task whose agent is working, as if the queue had started it.</summary>
    private async Task<Guid> RunningTaskAsync()
    {
        using var http = _app.CreateClient();
        var office = await (await http.PostAsJsonAsync("/api/offices", new { name = "E Corp" })).Content.ReadFromJsonAsync<JsonElement>();
        var officeId = office.GetProperty("id").GetGuid();
        var room = await (await http.PostAsJsonAsync($"/api/offices/{officeId}/rooms", new { name = "Web" })).Content.ReadFromJsonAsync<JsonElement>();
        var desk = await (await http.PostAsJsonAsync(
            $"/api/offices/{officeId}/rooms/{room.GetProperty("id").GetGuid()}/desks",
            new { name = "Frontend", agentName = "build" })).Content.ReadFromJsonAsync<JsonElement>();
        var task = await (await http.PostAsJsonAsync("/api/tasks", new
        {
            officeId,
            deskId = desk.GetProperty("id").GetGuid(),
            title = "Add a logout button",
        })).Content.ReadFromJsonAsync<JsonElement>();
        var taskId = new WorkTaskId(task.GetProperty("id").GetGuid());

        await using var scope = _app.Services.CreateAsyncScope();
        var tasks = scope.ServiceProvider.GetRequiredService<IWorkTaskRepository>();
        var running = (await tasks.FindAsync(taskId, default))!;
        running.Start(DateTimeOffset.UtcNow);
        running.AttachRun(AgentRun.Create("ses_mcp", "/srv/worktrees/mcp", "openspace/mcp"), DateTimeOffset.UtcNow);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(default);
        return taskId.Value;
    }

    private static string Text(CallToolResult result) =>
        string.Concat(result.Content.OfType<TextContentBlock>().Select(c => c.Text));

    [Fact]
    public async Task The_three_tools_are_listed()
    {
        await using var client = await ConnectAsync(null);

        var tools = await client.ListToolsAsync();

        Assert.Equal(["add_manual_check", "add_task_note", "announce", "complete_step", "get_task", "permission"], tools.Select(t => t.Name).Order());
    }

    [Fact]
    public async Task An_agent_leaves_a_note_on_its_own_task()
    {
        var taskId = await RunningTaskAsync();
        await using var client = await ConnectAsync(taskId);

        var result = await client.CallToolAsync("add_task_note", new Dictionary<string, object?> { ["text"] = "The header is shared with admin." });

        Assert.NotEqual(true, result.IsError);
        using var http = _app.CreateClient();
        var task = await http.GetFromJsonAsync<JsonElement>($"/api/tasks/{taskId}");
        var note = task.GetProperty("notes")[0];
        Assert.Equal(("Desk", "The header is shared with admin."), (note.GetProperty("authorKind").GetString(), note.GetProperty("text").GetString()));
    }

    [Fact]
    public async Task Claude_Code_waits_for_the_person_to_allow_a_tool()
    {
        var taskId = await RunningTaskAsync();
        await using var client = await ConnectAsync(taskId);
        using var http = _app.CreateClient();

        var asking = client.CallToolAsync("permission", new Dictionary<string, object?>
        {
            ["tool_name"] = "Bash",
            ["input"] = new Dictionary<string, object?> { ["command"] = "git push origin main" },
            ["tool_use_id"] = "toolu_1",
        }).AsTask();
        JsonElement waiting = default;
        for (var i = 0; i < 50 && waiting.ValueKind != JsonValueKind.Object; i++)
        {
            await Task.Delay(50);
            var activity = await http.GetFromJsonAsync<JsonElement>("/api/activity");
            waiting = activity.EnumerateArray().FirstOrDefault(a => a.GetProperty("kind").GetString() == "Waiting");
        }

        Assert.Equal(("Can I push to the remote?", "git push origin main"), (waiting.GetProperty("doing").GetString(), waiting.GetProperty("detail").GetString()));
        Assert.False(asking.IsCompleted);
        (await http.PostAsJsonAsync($"/api/tasks/{taskId}/permission", new { allow = true })).EnsureSuccessStatusCode();

        using var answer = JsonDocument.Parse(Text(await asking));
        Assert.Equal("allow", answer.RootElement.GetProperty("behavior").GetString());
        Assert.Equal("git push origin main", answer.RootElement.GetProperty("updatedInput").GetProperty("command").GetString());
    }

    [Fact]
    public async Task In_auto_mode_Claude_Code_runs_the_tests_without_asking_anyone()
    {
        var taskId = await RunningTaskAsync();
        await using var client = await ConnectAsync(taskId);
        using var http = _app.CreateClient();

        var result = await client.CallToolAsync("permission", new Dictionary<string, object?>
        {
            ["tool_name"] = "Bash",
            ["input"] = new Dictionary<string, object?> { ["command"] = "cd web && npm test" },
        });

        using var answer = JsonDocument.Parse(Text(result));
        Assert.Equal("allow", answer.RootElement.GetProperty("behavior").GetString());
        var log = await http.GetFromJsonAsync<JsonElement>($"/api/tasks/{taskId}/log");
        Assert.Contains(log.EnumerateArray(), e => e.GetProperty("summary").GetString() == "Allowed on its own: run the tests · cd web && npm test");
    }

    [Fact]
    public async Task An_agent_lists_what_a_person_must_try_by_hand()
    {
        var taskId = await RunningTaskAsync();
        await using var client = await ConnectAsync(taskId);
        using var http = _app.CreateClient();

        await client.CallToolAsync("add_manual_check", new Dictionary<string, object?> { ["what"] = "The logout button signs you out", ["how"] = "Open the site, sign in, tap Logout." });
        await client.CallToolAsync("add_manual_check", new Dictionary<string, object?> { ["what"] = "the logout button signs you out", ["how"] = "Again." });

        var task = await http.GetFromJsonAsync<JsonElement>($"/api/tasks/{taskId}");
        var check = Assert.Single(task.GetProperty("manualChecks").EnumerateArray());
        Assert.Equal((1, "Open the site, sign in, tap Logout."), (check.GetProperty("number").GetInt32(), check.GetProperty("how").GetString()));
    }

    [Fact]
    public async Task Without_a_task_a_permission_is_refused_at_once()
    {
        await using var client = await ConnectAsync(null);

        var result = await client.CallToolAsync("permission", new Dictionary<string, object?> { ["tool_name"] = "Bash", ["input"] = new Dictionary<string, object?>() });

        Assert.Contains("\"deny\"", Text(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_task_returns_the_brief()
    {
        var taskId = await RunningTaskAsync();
        await using var client = await ConnectAsync(taskId);

        var result = await client.CallToolAsync("get_task");

        Assert.Contains("Add a logout button", Text(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_the_task_header_the_tools_explain_what_is_missing()
    {
        await using var client = await ConnectAsync(null);

        var result = await client.CallToolAsync("announce", new Dictionary<string, object?> { ["text"] = "Hello" });

        Assert.True(result.IsError);
        Assert.Contains("X-Openspace-Task", Text(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_a_procedure_complete_step_explains_there_is_none()
    {
        var taskId = await RunningTaskAsync();
        await using var client = await ConnectAsync(taskId);

        var result = await client.CallToolAsync("complete_step", new Dictionary<string, object?> { ["step"] = 1, ["evidence"] = "Done" });

        Assert.True(result.IsError);
        Assert.Contains("no required procedure", Text(result), StringComparison.Ordinal);
    }
}
