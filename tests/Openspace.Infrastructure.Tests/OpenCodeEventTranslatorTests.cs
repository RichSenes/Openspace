using System.Text.Json;
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
    public void A_todo_list_with_nothing_in_progress_says_nothing()
    {
        Assert.Null(Translate("""{"type":"todo.updated","properties":{"sessionID":"ses_1","todos":[{"content":"Done","status":"completed"}]}}"""));
    }

    [Theory]
    [InlineData("edit", """{"filePath":"/srv/worktrees/abc/theme/header.php"}""", null, "Editing header.php")]
    [InlineData("read", """{"filePath":"/srv/worktrees/abc/index.html"}""", null, "Reading index.html")]
    [InlineData("write", """{"filePath":"/srv/worktrees/abc/new.css"}""", null, "Writing new.css")]
    [InlineData("bash", """{"command":"npm test"}""", "npm test", "Running npm test")]
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
    public void Long_phrases_are_cut_to_fit_a_bubble()
    {
        var command = new string('x', 200);
        var signal = (AgentSignal.Progress?)Translate(Tool("bash", "running", "{\"command\":\"" + command + "\"}"));

        Assert.Equal(80, signal?.Doing?.Length);
        Assert.EndsWith("…", signal?.Doing, StringComparison.Ordinal);
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

        Assert.Equal(new AgentSignal.PermissionAsked("ses_1", "per_1", "bash rm -rf build"), signal);
    }

    [Fact]
    public void Unrelated_events_are_ignored()
    {
        Assert.Null(Translate("""{"type":"server.heartbeat","properties":{}}"""));
        Assert.Null(Translate("""{"type":"file.edited","properties":{"file":"/a/b"}}"""));
    }
}
