using Openspace.Domain.Offices;
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

    private static string Answer(string usage, string parent = "null") =>
        "{\"type\":\"assistant\",\"parent_tool_use_id\":" + parent + ",\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"Looking at the header.\"}],\"usage\":" + usage + "}}";

    [Fact]
    public void An_answer_says_how_full_the_conversation_is_but_a_helper_answer_does_not()
    {
        var events = new ClaudeCodeEvents("claude-1", 200_000);
        const string Usage = """{"input_tokens":4,"cache_creation_input_tokens":1200,"cache_read_input_tokens":30000,"output_tokens":300}""";

        Assert.Equal(new AgentSignal.Context("claude-1", 31_504, 200_000), Assert.Single(events.Read(Answer(Usage))));
        Assert.Empty(events.Read(Answer(Usage, "\"toolu_9\"")));
    }

    [Fact]
    public void A_conversation_past_the_standard_window_runs_with_the_long_one()
    {
        var events = new ClaudeCodeEvents("claude-1", ClaudeCodeEvents.WindowOf("sonnet", null));

        var context = Assert.IsType<AgentSignal.Context>(Assert.Single(events.Read(Answer("""{"input_tokens":10,"cache_read_input_tokens":250000,"output_tokens":90}"""))));

        Assert.Equal(ClaudeCodeEvents.LongWindow, context.Window);
    }

    [Theory]
    [InlineData("sonnet", null, ClaudeCodeEvents.StandardWindow)]
    [InlineData(null, null, ClaudeCodeEvents.StandardWindow)]
    [InlineData("opus[1m]", null, ClaudeCodeEvents.LongWindow)]
    [InlineData("opus[1m]", 400_000L, 400_000L)]
    public void The_window_comes_from_the_options_else_from_the_model_name(string? model, long? configured, long expected)
    {
        Assert.Equal(expected, ClaudeCodeEvents.WindowOf(model, configured));
    }

    [Fact]
    public void A_summary_of_the_conversation_is_a_compaction()
    {
        var events = new ClaudeCodeEvents("claude-1");

        var signal = Assert.Single(events.Read("""{"type":"system","subtype":"compact_boundary","session_id":"abc","compact_metadata":{"trigger":"auto","pre_tokens":190000}}"""));

        Assert.Equal(new AgentSignal.Compacted("claude-1"), signal);
        Assert.Empty(events.Read("""{"type":"system","subtype":"init","session_id":"abc"}"""));
    }

    private static string HelperCall(string id, string name, string description, string parent = "null") =>
        "{\"type\":\"assistant\",\"parent_tool_use_id\":" + parent + ",\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"tool_use\",\"id\":\"" + id
        + "\",\"name\":\"" + name + "\",\"input\":{\"description\":\"" + description + "\",\"prompt\":\"Look around.\",\"subagent_type\":\"Explore\"}}]}}";

    [Theory]
    [InlineData("Agent")]
    [InlineData("Task")]
    public void A_hand_off_brings_a_helper_that_leaves_with_its_result(string tool)
    {
        var events = new ClaudeCodeEvents("claude-1");

        var arrived = Assert.Single(events.Read(HelperCall("toolu_7", tool, "Find the login form")));
        var back = events.Read(ToolResult("toolu_7", "The form is in login.php"));

        Assert.Equal(new AgentSignal.Delegated("claude-1", "toolu_7", "Find the login form"), arrived);
        Assert.Equal(new AgentSignal.Returned("claude-1", "toolu_7"), back[0]);
        Assert.Equal("A helper did: Find the login form", Assert.IsType<AgentSignal.ToolFinished>(back[1]).Summary);
    }

    [Fact]
    public void A_helper_handing_off_again_brings_no_new_intern()
    {
        var events = new ClaudeCodeEvents("claude-1");

        Assert.IsType<AgentSignal.Progress>(Assert.Single(events.Read(HelperCall("toolu_8", "Agent", "Deeper", "\"toolu_7\""))));
    }

    [Fact]
    public void Helpers_are_offered_only_to_roles_that_allow_them()
    {
        var (tools, allowed) = ClaudeCodeRuntime.Tools(new RolePermissions(PermissionLevel.Allow, PermissionLevel.Allow, PermissionLevel.Allow, PermissionLevel.Allow));
        var (without, _) = ClaudeCodeRuntime.Tools(new RolePermissions(PermissionLevel.Allow, PermissionLevel.Allow, PermissionLevel.Allow));

        Assert.Contains(ClaudeCodeEvents.HelperTool, tools);
        Assert.Contains(ClaudeCodeEvents.HelperTool, allowed);
        Assert.DoesNotContain(ClaudeCodeEvents.HelperTool, without);
    }

    [Fact]
    public void A_tool_call_shows_what_the_agent_does_and_its_result_goes_in_the_log()
    {
        var events = new ClaudeCodeEvents("claude-1");

        var started = Assert.Single(events.Read(ToolUse("toolu_1", "Bash", """{"command":"npm test","description":"Run the tests"}""")));
        var finished = Assert.Single(events.Read(ToolResult("toolu_1", "12 passed")));

        Assert.Equal(new AgentSignal.Progress("claude-1", null, "Running the tests"), started);
        var log = Assert.IsType<AgentSignal.ToolFinished>(finished);
        Assert.Equal((AuditKind.Command, "Ran the tests", "$ npm test\n12 passed", true), (log.Kind, log.Summary, log.Detail, log.Succeeded));
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
    public void The_result_reports_what_the_run_spent()
    {
        var events = new ClaudeCodeEvents("claude-1");

        var signals = events.Read("""{"type":"result","is_error":false,"result":"Done.","usage":{"input_tokens":12,"cache_creation_input_tokens":3000,"cache_read_input_tokens":45000,"output_tokens":800}}""");

        Assert.Equal(new AgentSignal.Used("claude-1", new TokenCount(12, 800, 45000, 3000)), signals[0]);
        Assert.Equal(new AgentSignal.Finished("claude-1"), signals[1]);
    }

    [Fact]
    public void An_error_result_fails_the_run_with_its_reason()
    {
        var events = new ClaudeCodeEvents("claude-1");

        var end = Assert.IsType<AgentSignal.Failed>(Assert.Single(events.Read("""{"type":"result","subtype":"error_during_execution","is_error":true,"result":"The model returned an invalid answer"}""")));

        Assert.Contains("invalid answer", end.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Running_out_of_the_account_s_limits_pauses_instead_of_failing()
    {
        var usage = new ClaudeCodeUsage(new FakeTimeProvider());
        var events = new ClaudeCodeEvents("claude-1");
        events.Read("""{"type":"rate_limit_event","rate_limit_info":{"status":"rejected","unifiedWindows":{"five_hour":{"utilization":1.0,"resetsAt":1792787400},"seven_day":{"utilization":0.7,"resetsAt":1793037600}}}}""", usage);

        var end = Assert.IsType<AgentSignal.OutOfLimits>(Assert.Single(events.Read("""{"type":"result","is_error":true,"result":"Usage limit reached, resets at 6pm"}""", usage)));

        Assert.Equal(("claude-code", DateTimeOffset.FromUnixTimeSeconds(1792787400)), (end.Scope, end.Until));
        Assert.IsType<AgentSignal.OutOfLimits>(ClaudeCodeEvents.Stopped("claude-2", "Claude Code stopped without finishing: You've hit your limit", null));
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

    private static ClaudeCodeRuntime Runtime(string command, bool enabled = true, Func<AgentSignal, Task>? dispatch = null) => new(
        Options.Create(new ClaudeCodeOptions { Command = command, Enabled = enabled }),
        Options.Create(new OpenCodeOptions { ToolsUrl = new Uri("http://127.0.0.1:5145/mcp") }),
        new PermissionRequests(),
        new ClaudeCodeUsage(TimeProvider.System),
        new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
        NullLogger<ClaudeCodeRuntime>.Instance,
        dispatch);

    [Fact]
    public async Task The_end_of_a_run_can_start_the_next_one_on_the_same_session()
    {
        // As a procedure reminder does: the run ends, and its end resumes the session at once.
        var finished = new List<AgentSignal>();
        var second = new TaskCompletionSource();
        ClaudeCodeRuntime? runtime = null;
        var setup = new AgentSetup("build", null, null, null, Domain.Offices.AgentEngine.ClaudeCode);
        string? session = null;
        runtime = Runtime(FakeClaude("""{"type":"result","is_error":false,"result":"Stopped early."}"""), dispatch: async signal =>
        {
            finished.Add(signal);
            if (finished.Count == 1)
            {
                await runtime!.PromptAsync(session!, _folder, setup, "Finish the procedure.", default);
            }
            else
            {
                second.TrySetResult();
            }
        });
        session = runtime.CreateSession(_folder);

        await runtime.PromptAsync(session, _folder, setup, "Do the task.", default);

        await second.Task.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.All(finished, s => Assert.IsType<AgentSignal.Finished>(s));
        Assert.Equal(2, finished.Count);
        runtime.Dispose();
    }

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
    public void Private_folders_are_denied_to_Claude_Code_reads_edits_and_commands()
    {
        using var runtime = Runtime("claude");
        var setup = new AgentSetup("build", null, null, new RolePermissions(PermissionLevel.Allow, PermissionLevel.Allow, PermissionLevel.Allow), Domain.Offices.AgentEngine.ClaudeCode, ["/srv/media/library"]);

        var args = runtime.SessionArguments(runtime.CreateSession("/srv/worktrees/a"), resume: false, setup, "/srv/worktrees/a");

        var denied = args.Skip(args.IndexOf("--disallowedTools") + 1).Take(3).ToList();
        Assert.Equal(["Read(//srv/media/library/**)", "Edit(//srv/media/library/**)", "Bash(*/srv/media/library*)"], denied);
        Assert.Empty(ClaudeCodeRuntime.Forbidden([]));
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

        Assert.Equal(new AgentAction(ActionKind.Run, "npm test"), ClaudeCodePermissions.Action("Bash", input.RootElement));
        Assert.Equal(new AgentAction(ActionKind.Edit, "/a/header.php"), ClaudeCodePermissions.Action("Edit", JsonDocument.Parse("""{"file_path":"/a/header.php"}""").RootElement));
        using var allow = JsonDocument.Parse(ClaudeCodePermissions.Reply(true, input.RootElement));
        using var deny = JsonDocument.Parse(ClaudeCodePermissions.Reply(false, input.RootElement));
        Assert.Equal(("allow", "npm test"), (allow.RootElement.GetProperty("behavior").GetString(), allow.RootElement.GetProperty("updatedInput").GetProperty("command").GetString()));
        Assert.Equal("deny", deny.RootElement.GetProperty("behavior").GetString());
    }
}
