using System.Text.Json;
using Openspace.Application.Agents;
using Openspace.Domain.Work;
using Openspace.Infrastructure.OpenCode;

namespace Openspace.Infrastructure.Tests;

/// <summary>Payloads shaped like real OpenCode events; paths and ids are made up.</summary>
public class OpenCodeEventTranslatorTests
{
    private static AgentSignal? Translate(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return OpenCodeEventTranslator.Translate(doc.RootElement);
    }

    private static string Tool(string tool, string status, string input, string? title = null)
    {
        var titleJson = title is null ? string.Empty : ",\"title\":\"" + title + "\"";
        return "{\"type\":\"message.part.updated\",\"properties\":{\"sessionID\":\"ses_1\",\"part\":{\"type\":\"tool\",\"tool\":\""
            + tool + "\",\"sessionID\":\"ses_1\",\"state\":{\"status\":\"" + status + "\",\"input\":" + input + titleJson + "}}}}";
    }

    [Fact]
    public void The_todo_item_in_progress_becomes_the_step()
    {
        var signal = Translate("""
            {"type":"todo.updated","properties":{"sessionID":"ses_1","todos":[
              {"content":"Read the header","status":"completed"},
              {"content":"Adding the logout button to the header","status":"in_progress"},
              {"content":"Check the result","status":"pending"}]}}
            """);

        Assert.Equal(new AgentSignal.Progress("ses_1", "Adding the logout button to the header", null), signal);
    }

    [Fact]
    public void A_summarised_conversation_is_reported()
    {
        Assert.Equal(new AgentSignal.Compacted("ses_1"), Translate("""{"type":"session.compacted","properties":{"sessionID":"ses_1"}}"""));
    }

    [Fact]
    public void A_todo_list_with_nothing_in_progress_says_nothing()
    {
        Assert.Null(Translate("""{"type":"todo.updated","properties":{"sessionID":"ses_1","todos":[{"content":"Done","status":"completed"}]}}"""));
    }

    [Theory]
    [InlineData("edit", """{"filePath":"/srv/worktrees/abc/theme/header.php"}""", null, "Editing header.php")]
    [InlineData("read", """{"filePath":"/srv/worktrees/abc/index.html"}""", null, "Reading index.html")]
    [InlineData("write", """{"filePath":"/srv/worktrees/abc/new.css"}""", null, "Writing new.css")]
    [InlineData("bash", """{"command":"npm test"}""", "npm test", "Running the tests")]
    [InlineData("grep", """{"pattern":"logout"}""", null, "Searching the code")]
    [InlineData("mystery", "{}", null, "Using mystery")]
    public void A_running_tool_becomes_a_short_phrase(string tool, string input, string? title, string expected)
    {
        var signal = Translate(Tool(tool, "running", input, title));

        Assert.Equal(new AgentSignal.Progress("ses_1", null, expected), signal);
    }

    [Fact]
    public void Pending_and_completed_tools_and_todo_writes_are_not_news()
    {
        Assert.Null(Translate(Tool("edit", "pending", "{}")));
        Assert.Null(Translate(Tool("edit", "completed", """{"filePath":"/a/b.php"}""")));
        Assert.Null(Translate(Tool("todowrite", "running", "{}")));
    }

    [Fact]
    public void A_command_nothing_recognises_still_reads_as_a_sentence()
    {
        var command = new string('x', 200);
        var signal = (AgentSignal.Progress?)Translate(Tool("bash", "running", "{\"command\":\"" + command + "\"}"));

        Assert.Equal("Running a command", signal?.Doing);
    }

    [Fact]
    public void A_finished_answer_reports_its_tokens_once_complete()
    {
        const string Answer = """{"type":"message.updated","properties":{"info":{"id":"msg_1","sessionID":"ses_1","role":"assistant","time":{"created":1,COMPLETED},"tokens":{"input":900,"output":120,"reasoning":30,"cache":{"read":4000,"write":250}}}}}""";

        Assert.Null(Translate(Answer.Replace("COMPLETED", "\"x\":0", StringComparison.Ordinal)));
        Assert.Equal(
            new AgentSignal.Used("ses_1", new TokenCount(900, 150, 4000, 250), "msg_1"),
            Translate(Answer.Replace("COMPLETED", "\"completed\":2", StringComparison.Ordinal)));
        Assert.Null(Translate("""{"type":"message.updated","properties":{"info":{"id":"msg_0","sessionID":"ses_1","role":"user","time":{"created":1,"completed":2},"tokens":{}}}}"""));
    }

    [Fact]
    public void A_finished_answer_names_its_model_so_its_window_can_be_looked_up()
    {
        var signal = Translate("""{"type":"message.updated","properties":{"info":{"id":"msg_2","sessionID":"ses_1","role":"assistant","providerID":"opencode","modelID":"free-model","time":{"created":1,"completed":2},"tokens":{"input":10,"output":5,"reasoning":0,"cache":{"read":100,"write":0}}}}}""");

        Assert.Equal("opencode/free-model", Assert.IsType<AgentSignal.Used>(signal).Model);
    }

    [Fact]
    public void The_task_tool_brings_a_helper_while_it_runs_and_sends_it_away_when_it_ends()
    {
        static AgentSignal? Of(string tool, string status)
        {
            using var doc = JsonDocument.Parse("{\"type\":\"message.part.updated\",\"properties\":{\"part\":{\"type\":\"tool\",\"tool\":\"" + tool
                + "\",\"callID\":\"call_5\",\"sessionID\":\"ses_1\",\"state\":{\"status\":\"" + status + "\",\"input\":{\"description\":\"Map the routes\"}}}}}");
            return OpenCodeEventTranslator.Delegation(doc.RootElement);
        }

        Assert.Equal(new AgentSignal.Delegated("ses_1", "call_5", "Map the routes"), Of("task", "running"));
        Assert.Equal(new AgentSignal.Returned("ses_1", "call_5"), Of("task", "completed"));
        Assert.Equal(new AgentSignal.Returned("ses_1", "call_5"), Of("task", "error"));
        Assert.Null(Of("task", "pending"));
        Assert.Null(Of("bash", "running"));
    }

    [Fact]
    public void Idle_means_finished()
    {
        Assert.Equal(new AgentSignal.Finished("ses_1"), Translate("""{"type":"session.idle","properties":{"sessionID":"ses_1"}}"""));
    }

    [Fact]
    public void Errors_fail_except_when_Openspace_aborted_on_purpose()
    {
        var failed = Translate("""{"type":"session.error","properties":{"sessionID":"ses_1","error":{"name":"APIError","data":{"message":"rate limited"}}}}""");
        var aborted = Translate("""{"type":"session.error","properties":{"sessionID":"ses_1","error":{"name":"MessageAbortedError","data":{}}}}""");

        Assert.Equal(new AgentSignal.Failed("ses_1", "The agent stopped with an error: rate limited"), failed);
        Assert.Null(aborted);
    }

    [Fact]
    public void A_permission_request_carries_what_is_asked()
    {
        var signal = Translate("""
            {"type":"permission.asked","properties":{"id":"per_1","sessionID":"ses_1","permission":"bash","patterns":["rm -rf build"],"metadata":{},"always":[]}}
            """);

        Assert.Equal(new AgentSignal.PermissionAsked("ses_1", "per_1", new AgentAction(ActionKind.Run, "rm -rf build")), signal);
    }

    [Fact]
    public void Unrelated_events_are_ignored()
    {
        Assert.Null(Translate("""{"type":"server.heartbeat","properties":{}}"""));
        Assert.Null(Translate("""{"type":"file.edited","properties":{"file":"/a/b"}}"""));
    }

    private static string Done(string tool, string status, string input, string extra) =>
        "{\"type\":\"message.part.updated\",\"properties\":{\"sessionID\":\"ses_1\",\"part\":{\"type\":\"tool\",\"tool\":\""
        + tool + "\",\"callID\":\"call_7\",\"sessionID\":\"ses_1\",\"state\":{\"status\":\"" + status + "\",\"input\":" + input + extra + "}}}}";

    [Fact]
    public void A_finished_edit_is_logged_as_a_change_with_its_diff()
    {
        var signal = Translate(Done("edit", "completed", """{"filePath":"/srv/worktrees/abc/header.php"}""",
            ""","output":"Edit applied.","metadata":{"diff":"-old\n+new"}"""));

        Assert.Equal(new AgentSignal.ToolFinished("ses_1", "call_7", AuditKind.Change, "Changed header.php", "-old\n+new", true), signal);
    }

    [Fact]
    public void A_command_keeps_its_line_and_output_and_fails_on_a_nonzero_exit()
    {
        var signal = (AgentSignal.ToolFinished?)Translate(Done("bash", "completed", """{"command":"npm test"}""",
            ""","title":"npm test","output":"1 failing","metadata":{"exit":1}"""));

        Assert.Equal((AuditKind.Command, "Ran the tests", "$ npm test\n1 failing", false),
            (signal?.Kind, signal?.Summary, signal?.Detail, signal?.Succeeded));
    }

    [Fact]
    public void A_tool_error_is_logged_as_failed_with_the_error()
    {
        var signal = (AgentSignal.ToolFinished?)Translate(Done("read", "error", """{"filePath":"/srv/worktrees/abc/missing.php"}""",
            ",\"error\":\"File not found\""));

        Assert.False(signal?.Succeeded);
        Assert.Contains("File not found", signal?.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Todo_list_writes_are_not_logged_as_tools()
    {
        Assert.Null(Translate(Done("todowrite", "completed", "{}", ",\"output\":\"[]\"")));
    }

    [Fact]
    public void Calls_to_the_Openspace_tools_are_not_logged_twice()
    {
        Assert.Null(Translate(Tool("openspace_announce", "running", """{"text":"Adding the button"}""")));
        Assert.Null(Translate(Done("openspace_add_task_note", "completed", """{"text":"Found it"}""", ",\"output\":\"Note added\"")));
    }

    [Theory]
    [InlineData("once", true)]
    [InlineData("always", true)]
    [InlineData("reject", false)]
    public void A_permission_reply_is_recognised(string reply, bool allowed)
    {
        var signal = Translate("{\"type\":\"permission.replied\",\"properties\":{\"sessionID\":\"ses_1\",\"requestID\":\"per_1\",\"reply\":\"" + reply + "\"}}");

        Assert.Equal(new AgentSignal.PermissionReplied("ses_1", "per_1", allowed), signal);
    }

    [Theory]
    [InlineData("edit", "header.php", ActionKind.Edit, "header.php")]
    [InlineData("external_directory", "/srv/other", ActionKind.OutsideFolder, "/srv/other")]
    [InlineData("webfetch", "https://example.org", ActionKind.Fetch, "https://example.org")]
    [InlineData("question", "", ActionKind.Other, "question")]
    public void Permission_requests_say_what_kind_of_action_they_are(string permission, string pattern, ActionKind kind, string target)
    {
        var patterns = pattern.Length > 0 ? "[\"" + pattern + "\"]" : "[]";
        var signal = (AgentSignal.PermissionAsked?)Translate(
            "{\"type\":\"permission.asked\",\"properties\":{\"id\":\"per_1\",\"sessionID\":\"ses_1\",\"permission\":\"" + permission + "\",\"patterns\":" + patterns + ",\"metadata\":{},\"always\":[]}}");

        Assert.Equal(new AgentAction(kind, target), signal?.Action);
    }

    [Fact]
    public void A_provider_over_its_free_limit_pauses_until_its_next_try()
    {
        var signal = Translate("""
            {"type":"session.status","properties":{"sessionID":"ses_1","status":{"type":"retry","attempt":1,"message":"Free usage exceeded, subscribe to Go",
             "action":{"reason":"free_tier_limit","provider":"opencode","title":"Free limit reached"},"next":1792886400550}}}
            """);

        var paused = Assert.IsType<AgentSignal.OutOfLimits>(signal);
        Assert.Equal(("opencode:opencode", DateTimeOffset.FromUnixTimeMilliseconds(1792886400550)), (paused.Scope, paused.Until));
        Assert.Contains("Free limit reached", paused.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Other_retries_show_in_the_bubble_and_a_busy_session_is_not_news()
    {
        var retry = Translate("""{"type":"session.status","properties":{"sessionID":"ses_1","status":{"type":"retry","attempt":3,"message":"Overloaded","next":1792886400550}}}""");

        Assert.Equal(new AgentSignal.Progress("ses_1", null, "The model is not answering: trying again (3)"), retry);
        Assert.Null(Translate("""{"type":"session.status","properties":{"sessionID":"ses_1","status":{"type":"busy"}}}"""));
    }

    [Fact]
    public void A_session_already_retrying_is_read_from_the_status_list()
    {
        using var statuses = JsonDocument.Parse("""
            {"ses_1":{"type":"retry","attempt":1,"message":"Free usage exceeded, subscribe to Go","action":{"reason":"free_tier_limit","provider":"opencode","title":"Free limit reached"},"next":1792886400550},
             "ses_2":{"type":"busy"}}
            """);

        Assert.IsType<AgentSignal.OutOfLimits>(OpenCodeEventTranslator.FromStatusOf(statuses.RootElement, "ses_1"));
        Assert.Null(OpenCodeEventTranslator.FromStatusOf(statuses.RootElement, "ses_2"));
        Assert.Null(OpenCodeEventTranslator.FromStatusOf(statuses.RootElement, "ses_3"));
    }
}
