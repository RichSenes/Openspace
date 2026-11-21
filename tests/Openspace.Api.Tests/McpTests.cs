using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Openspace.Application.Abstractions;
using Openspace.Domain.Work;

namespace Openspace.Api.Tests;

/// <summary>Talks to the real /mcp endpoint with the official MCP client, as OpenCode would.</summary>
public sealed class McpTests : IAsyncLifetime
{
    private readonly OpenspaceApp _app = new(work: new ListedWork());

    public Task InitializeAsync() => Task.CompletedTask;

    // xUnit 2 never calls IAsyncDisposable on a test class: through IAsyncLifetime the host and its temporary folders go.
    public async Task DisposeAsync() => await _app.DisposeAsync();

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
    public async Task The_tools_are_listed()
    {
        await using var client = await ConnectAsync(null);

        var tools = await client.ListToolsAsync();

        Assert.Equal(
            ["add_manual_check", "add_task_note", "announce", "brain_journal", "brain_now", "brain_propose", "brain_read", "brain_search", "complete_step", "get_task", "permission", "wait_for_person"],
            tools.Select(t => t.Name).Order());
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
    public async Task An_agent_waits_for_the_person_until_they_say_go_on()
    {
        var taskId = await RunningTaskAsync();
        await using var client = await ConnectAsync(taskId);
        using var http = _app.CreateClient();

        var waitingForYou = client.CallToolAsync("wait_for_person", new Dictionary<string, object?>
        {
            ["question"] = "The app is open on this PC: close it, then press Done so I can install the new version.",
        }).AsTask();
        JsonElement waiting = default;
        for (var i = 0; i < 50 && waiting.ValueKind != JsonValueKind.Object; i++)
        {
            await Task.Delay(50);
            var activity = await http.GetFromJsonAsync<JsonElement>("/api/activity");
            waiting = activity.EnumerateArray().FirstOrDefault(a => a.GetProperty("kind").GetString() == "Waiting");
        }

        Assert.True(waiting.GetProperty("question").GetBoolean());
        Assert.StartsWith("The app is open on this PC", waiting.GetProperty("doing").GetString(), StringComparison.Ordinal);
        await Task.Delay(300);
        Assert.False(waitingForYou.IsCompleted);
        (await http.PostAsJsonAsync($"/api/tasks/{taskId}/permission", new { allow = true })).EnsureSuccessStatusCode();

        Assert.Equal("The person answered: done, go on.", Text(await waitingForYou));
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

    private static async Task<string> GitAsync(string folder, params string[] args)
    {
        var start = new System.Diagnostics.ProcessStartInfo("git") { WorkingDirectory = folder, RedirectStandardOutput = true };
        args.ToList().ForEach(start.ArgumentList.Add);
        using var git = System.Diagnostics.Process.Start(start)!;
        var output = await git.StandardOutput.ReadToEndAsync();
        await git.WaitForExitAsync();
        return output.Trim();
    }

    [Fact]
    public async Task An_agent_writes_in_its_own_journal_and_the_vault_commits_it_as_the_desk()
    {
        var taskId = await RunningTaskAsync();
        await using var client = await ConnectAsync(taskId);

        var result = await client.CallToolAsync("brain_journal", new Dictionary<string, object?> { ["text"] = "Added the button; the header is shared with admin." });

        Assert.NotEqual(true, result.IsError);
        Assert.Equal("Written in Inhabitants/Frontend/journal.md.", Text(result));
        var journal = await File.ReadAllTextAsync(Path.Combine(_app.BrainFolder, "Inhabitants", "Frontend", "journal.md"));
        Assert.StartsWith("---\nkind: journal\nauthor: Frontend\n---\n# Frontend's journal\n", journal, StringComparison.Ordinal);
        Assert.Contains("Add a logout button: Added the button; the header is shared with admin.", journal, StringComparison.Ordinal);
        Assert.Equal("Frontend: wrote in its journal", await GitAsync(_app.BrainFolder, "log", "-1", "--format=%s", "--", "Inhabitants/Frontend/journal.md"));
        Assert.Equal("Frontend", await GitAsync(_app.BrainFolder, "log", "-1", "--format=%an"));
    }

    [Fact]
    public async Task An_agent_proposes_a_decision_others_then_find_and_read()
    {
        var taskId = await RunningTaskAsync();
        await using var client = await ConnectAsync(taskId);

        var proposed = await client.CallToolAsync("brain_propose", new Dictionary<string, object?>
        {
            ["kind"] = "decision",
            ["title"] = "Logout lives in the header",
            ["body"] = "Every page shows Logout at the top right.",
            ["reasons"] = "People looked for it there.",
            ["rooms"] = new List<string> { "Web" },
        });
        Assert.NotEqual(true, proposed.IsError);
        var path = $"Decisions/{DateTime.Now:yyyy-MM-dd}-logout-lives-in-the-header.md";
        Assert.Equal($"Proposed as {path}. It is not in force until it is accepted.", Text(proposed));

        var read = Text(await client.CallToolAsync("brain_read", new Dictionary<string, object?> { ["note"] = path }));
        // Shiori's acceptor runs in the background and may already have accepted it: either is right.
        Assert.Matches("^---\nkind: decision\nstatus: (proposed|accepted)\noffices: \\[E Corp\\]\nrooms: \\[Web\\]\n", read);
        Assert.Contains($"from: task {taskId}\nauthor: Frontend\n", read, StringComparison.Ordinal);

        // The watcher reads the new note into the index within moments.
        var found = "";
        for (var i = 0; i < 50 && !found.Contains(path, StringComparison.Ordinal); i++)
        {
            await Task.Delay(100);
            found = Text(await client.CallToolAsync("brain_search", new Dictionary<string, object?> { ["query"] = "logout header", ["kind"] = "decision" }));
        }

        Assert.Matches($"- {System.Text.RegularExpressions.Regex.Escape(path)} · decision, (proposed|accepted) · Logout lives in the header", found);
    }

    [Fact]
    public async Task Reading_outside_the_vault_is_refused()
    {
        var taskId = await RunningTaskAsync();
        await using var client = await ConnectAsync(taskId);

        var result = await client.CallToolAsync("brain_read", new Dictionary<string, object?> { ["note"] = "../../etc/passwd" });

        Assert.True(result.IsError);
        Assert.Contains("not inside the vault", Text(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_agent_sees_who_else_is_working_and_its_brief_reads_the_brain()
    {
        var taskId = await RunningTaskAsync();
        await using var client = await ConnectAsync(taskId);
        using var http = _app.CreateClient();
        var office = (await http.GetFromJsonAsync<JsonElement>($"/api/tasks/{taskId}")).GetProperty("officeId").GetGuid();
        ListedWork.Items.Add(new(Guid.NewGuid(), office, "E Corp", "Web", "Screens", "Restyle the footer", ["src/web/footer.css"]));

        var now = Text(await client.CallToolAsync("brain_now"));
        var brief = Text(await client.CallToolAsync("get_task"));

        Assert.Contains("\"Restyle the footer\" at the desk \"Screens\" (room Web): changing src/web/footer.css", now, StringComparison.Ordinal);
        Assert.Contains("Before you start:", brief, StringComparison.Ordinal);
        Assert.Contains("1. Read before acting", brief, StringComparison.Ordinal);
        Assert.Contains("Restyle the footer", brief, StringComparison.Ordinal);
    }

    /// <summary>Work in flight the test sets, shared by every host the class starts.</summary>
    private sealed class ListedWork : Openspace.Application.Brain.IWorkInProgress
    {
        public static List<Openspace.Application.Brain.WorkInFlight> Items { get; } = [];

        public IReadOnlyList<Openspace.Application.Brain.WorkInFlight> Current() => [.. Items];
    }
}
