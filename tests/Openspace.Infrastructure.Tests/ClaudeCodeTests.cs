using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Openspace.Application.Abstractions;
using Openspace.Application.Agents;
using Openspace.Domain.Roles;
using Openspace.Domain.Work;
using Openspace.Infrastructure.ClaudeCode;
using Openspace.Infrastructure.OpenCode;

namespace Openspace.Infrastructure.Tests;

/// <summary>Lines shaped like Claude Code's stream-json output; paths, ids and texts are made up.</summary>
public class ClaudeCodeEventsTests
{
    private static string ToolUse(string id, string name, string input) =>
        "{\"type\":\"assistant\",\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"tool_use\",\"id\":\"" + id
        + "\",\"name\":\"" + name + "\",\"input\":" + input + "}]}}";

    private static string ToolResult(string id, string content, bool error = false) =>
        "{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"tool_result\",\"tool_use_id\":\"" + id
        + "\",\"content\":" + JsonSerializer.Serialize(content) + ",\"is_error\":" + (error ? "true" : "false") + "}]}}";

    [Fact]
    public void A_tool_call_shows_what_the_agent_does_and_its_result_goes_in_the_log()
    {
        var events = new ClaudeCodeEvents("claude-1");

        var started = Assert.Single(events.Read(ToolUse("toolu_1", "Bash", """{"command":"npm test","description":"Run the tests"}""")));
        var finished = Assert.Single(events.Read(ToolResult("toolu_1", "12 passed")));

        Assert.Equal(new AgentSignal.Progress("claude-1", null, "Running Run the tests"), started);
        var log = Assert.IsType<AgentSignal.ToolFinished>(finished);
        Assert.Equal((AuditKind.Command, "Ran Run the tests", "$ npm test\n12 passed", true), (log.Kind, log.Summary, log.Detail, log.Succeeded));
    }

    [Fact]
    public void An_edit_is_logged_as_a_change_with_its_diff()
    {
        var events = new ClaudeCodeEvents("claude-1");
        events.Read(ToolUse("toolu_2", "Edit", """{"file_path":"/srv/worktrees/a/header.php","old_string":"<nav>","new_string":"<nav class=\"top\">"}"""));

        var log = Assert.IsType<AgentSignal.ToolFinished>(Assert.Single(events.Read(ToolResult("toolu_2", "ok"))));

        Assert.Equal((AuditKind.Change, "Changed header.php"), (log.Kind, log.Summary));
        Assert.Contains("-<nav>", log.Detail, StringComparison.Ordinal);
        Assert.Contains("+<nav class=\"top\">", log.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void The_todo_in_progress_becomes_the_step_and_is_not_logged_as_a_tool()
    {
        var events = new ClaudeCodeEvents("claude-1");

        var step = Assert.Single(events.Read(ToolUse("toolu_3", "TodoWrite", """{"todos":[{"content":"Read the header","status":"completed","activeForm":"Reading the header"},{"content":"Add the button","status":"in_progress","activeForm":"Adding the button"}]}""")));

        Assert.Equal(new AgentSignal.Progress("claude-1", "Adding the button", null), step);
        Assert.Empty(events.Read(ToolResult("toolu_3", "Todos updated")));
    }

    [Fact]
    public void Openspace_tools_are_neither_shown_nor_logged_twice()
    {
        var events = new ClaudeCodeEvents("claude-1");

        Assert.Empty(events.Read(ToolUse("toolu_4", "mcp__openspace__announce", """{"text":"Hello"}""")));
        Assert.Empty(events.Read(ToolResult("toolu_4", "Announced.")));
    }

    [Fact]
    public void The_result_ends_the_run_with_the_agent_s_summary()
    {
        var events = new ClaudeCodeEvents("claude-1");

        var end = Assert.Single(events.Read("""{"type":"result","subtype":"success","is_error":false,"result":"Added the logout button."}"""));

        Assert.Equal(new AgentSignal.Finished("claude-1"), end);
        Assert.Equal(("Added the logout button.", true), (events.LastReply, events.Ended));
    }

    [Fact]
    public void An_error_result_fails_the_run_with_its_reason()
    {
        var events = new ClaudeCodeEvents("claude-1");

        var end = Assert.IsType<AgentSignal.Failed>(Assert.Single(events.Read("""{"type":"result","subtype":"success","is_error":true,"result":"Usage limit reached, resets at 6pm"}""")));

        Assert.Contains("Usage limit reached", end.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Limits_are_remembered_and_other_lines_ignored()
    {
        var usage = new ClaudeCodeUsage(new FakeTimeProvider());
        var events = new ClaudeCodeEvents("claude-1");

        Assert.Empty(events.Read("""{"type":"rate_limit_event","rate_limit_info":{"status":"allowed","unifiedWindows":{"five_hour":{"utilization":0.26,"resetsAt":1792787400},"seven_day":{"utilization":0.51,"resetsAt":1793037600}}}}""", usage));
        Assert.Empty(events.Read("""{"type":"system","subtype":"init","session_id":"x"}"""));
        Assert.Empty(events.Read("Warning: something on stdout"));

        Assert.Equal((0.26, 0.51), (usage.Latest!.FiveHours!.Used, usage.Latest.Week!.Used));
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1792787400), usage.Latest.FiveHours.ResetsAt);
    }
}

public class ClaudeCodeRuntimeTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("openspace-claude-").FullName;

    public void Dispose()
    {
        Directory.Delete(_folder, recursive: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>A stand-in for the claude command: prints what it is given to print.</summary>
    private string FakeClaude(string output, int exitCode = 0)
    {
        var path = Path.Combine(_folder, "claude");
        File.WriteAllText(path, $"#!/bin/sh\ncat > /dev/null\nprintf '%s\\n' '{output.Replace("'", "'\\''", StringComparison.Ordinal)}'\nexit {exitCode}\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        return path;
    }

    private static ClaudeCodeRuntime Runtime(string command, bool enabled = true) => new(
        Options.Create(new ClaudeCodeOptions { Command = command, Enabled = enabled }),
        Options.Create(new OpenCodeOptions { ToolsUrl = new Uri("http://127.0.0.1:5145/mcp") }),
        new PermissionRequests(),
        new ClaudeCodeUsage(TimeProvider.System),
        new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
        NullLogger<ClaudeCodeRuntime>.Instance);

    [Fact]
    public void A_role_gets_its_tools_and_uses_without_asking_only_what_it_allows()
    {
        var (tools, allowed) = ClaudeCodeRuntime.Tools(new RolePermissions(PermissionLevel.Allow, PermissionLevel.Ask, PermissionLevel.Deny));

        Assert.Equal(["Read", "Grep", "Glob", "TodoWrite", "Edit", "Write", "NotebookEdit", "Bash"], tools);
        Assert.Equal(["Read", "Grep", "Glob", "TodoWrite", "mcp__openspace", "Edit", "Write", "NotebookEdit"], allowed);
    }

    [Fact]
    public void An_agent_runs_restricted_with_Openspace_answering_its_permissions()
    {
        using var runtime = Runtime("claude");
        var session = runtime.CreateSession("/srv/worktrees/a");
        var taskId = Guid.NewGuid();
        runtime.ConnectTools("/srv/worktrees/a", taskId);
        var setup = new AgentSetup("build", "sonnet", "Be brief.", null, Domain.Offices.AgentEngine.ClaudeCode);

        var first = runtime.SessionArguments(session, resume: false, setup, "/srv/worktrees/a");
        var later = runtime.SessionArguments(session, resume: true, setup, "/srv/worktrees/a");

        Assert.Contains("--restricted", first);
        Assert.Equal(ClaudeCodeRuntime.PermissionTool, first[first.IndexOf("--permission-prompt-tool") + 1]);
        Assert.Contains(taskId.ToString(), first[first.IndexOf("--mcp-config") + 1], StringComparison.Ordinal);
        Assert.Equal(("sonnet", "Be brief."), (first[first.IndexOf("--model") + 1], first[first.IndexOf("--append-system-prompt") + 1]));
        Assert.Equal(session["claude-".Length..], first[first.IndexOf("--session-id") + 1]);
        Assert.Equal(session["claude-".Length..], later[later.IndexOf("--resume") + 1]);
    }

    [Fact]
    public void The_structured_answer_is_read_from_the_last_line()
    {
        var output = "Warning: no stdin\n" + """{"type":"result","is_error":false,"result":"done","structured_output":{"tasks":[]}}""";

        Assert.Equal("""{"tasks":[]}""", ClaudeCodeRuntime.Answer(output, structured: true));
        Assert.Equal("done", ClaudeCodeRuntime.Answer(output, structured: false));
        Assert.Throws<InvalidOperationException>(() => ClaudeCodeRuntime.Answer("""{"type":"result","is_error":true,"result":"limit"}""", structured: false));
    }

    [Fact]
    public async Task A_question_runs_the_command_in_the_folder_and_returns_its_answer()
    {
        using var runtime = Runtime(FakeClaude("""{"type":"result","is_error":false,"result":"ok","structured_output":{"verdict":"ok"}}"""));

        var answer = await runtime.AskAsync(_folder, "Review.", "The task", "{}", ["Read"], ["Read"], "haiku", default);

        Assert.Equal("""{"verdict":"ok"}""", answer);
    }

    [Fact]
    public async Task Claude_Code_is_there_only_when_its_command_answers_and_it_is_enabled()
    {
        using var present = Runtime(FakeClaude("2.1.0 (Claude Code)"));
        using var missing = Runtime(Path.Combine(_folder, "nowhere"));
        using var disabled = Runtime(FakeClaude("2.1.0 (Claude Code)"), enabled: false);

        Assert.True(await present.IsAvailableAsync(default));
        Assert.False(await missing.IsAvailableAsync(default));
        Assert.False(await disabled.IsAvailableAsync(default));
    }

    [Fact]
    public void Permissions_are_described_and_answered_as_Claude_Code_expects()
    {
        using var input = JsonDocument.Parse("""{"command":"npm test"}""");

        Assert.Equal("run npm test", ClaudeCodePermissions.Describe("Bash", input.RootElement));
        Assert.Equal("edit header.php", ClaudeCodePermissions.Describe("Edit", JsonDocument.Parse("""{"file_path":"/a/header.php"}""").RootElement));
        using var allow = JsonDocument.Parse(ClaudeCodePermissions.Reply(true, input.RootElement));
        using var deny = JsonDocument.Parse(ClaudeCodePermissions.Reply(false, input.RootElement));
        Assert.Equal(("allow", "npm test"), (allow.RootElement.GetProperty("behavior").GetString(), allow.RootElement.GetProperty("updatedInput").GetProperty("command").GetString()));
        Assert.Equal("deny", deny.RootElement.GetProperty("behavior").GetString());
    }
}
