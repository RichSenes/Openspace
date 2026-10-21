using System.Net;
using System.Text;
using System.Text.Json;
using Openspace.Application.Abstractions;
using Openspace.Domain.Roles;
using Openspace.Infrastructure.OpenCode;

namespace Openspace.Infrastructure.Tests;

public class OpenCodeRuntimeTests
{
    /// <summary>A pretend OpenCode server that records requests and answers from a script.</summary>
    private sealed class FakeServer(Func<HttpRequestMessage, (HttpStatusCode, string)> answer) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string PathAndQuery, string? Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.Method, request.RequestUri!.PathAndQuery, body));
            var (status, json) = answer(request);
            return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json"), RequestMessage = request };
        }
    }

    private static (OpenCodeRuntime Runtime, FakeServer Server) Create(Func<HttpRequestMessage, (HttpStatusCode, string)> answer)
    {
        var server = new FakeServer(answer);
        var http = new HttpClient(server) { BaseAddress = new Uri("http://opencode.test/") };
        var options = Microsoft.Extensions.Options.Options.Create(new OpenCodeOptions { ToolsUrl = new Uri("http://openspace.test/mcp") });
        var workspaces = Microsoft.Extensions.Options.Options.Create(new Git.WorkspaceOptions { Root = Path.Combine(Path.GetTempPath(), "openspace-runtime-tests") });
        return (new OpenCodeRuntime(http, options, workspaces), server);
    }

    private const string Workspace = "/srv/worktrees/abc";

    [Fact]
    public async Task Prompts_name_the_folder_the_agent_and_the_split_model()
    {
        var (runtime, server) = Create(_ => (HttpStatusCode.NoContent, ""));

        await runtime.PromptAsync("ses_1", Workspace, new AgentSetup("build", "opencode/deepseek-v4.1-flash-free", null, null), "Do it", default);

        var (method, path, body) = Assert.Single(server.Requests);
        Assert.Equal(HttpMethod.Post, method);
        Assert.Equal("/session/ses_1/prompt_async?directory=%2Fsrv%2Fworktrees%2Fabc", path);
        using var json = JsonDocument.Parse(body!);
        Assert.Equal("build", json.RootElement.GetProperty("agent").GetString());
        Assert.Equal("opencode", json.RootElement.GetProperty("model").GetProperty("providerID").GetString());
        Assert.Equal("deepseek-v4.1-flash-free", json.RootElement.GetProperty("model").GetProperty("modelID").GetString());
        Assert.Equal("Do it", json.RootElement.GetProperty("parts")[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task Without_a_model_the_agent_default_is_used()
    {
        var (runtime, server) = Create(_ => (HttpStatusCode.NoContent, ""));

        await runtime.PromptAsync("ses_1", Workspace, new AgentSetup("build", null, null, null), "Do it", default);

        Assert.DoesNotContain("\"model\"", server.Requests[0].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sessions_are_created_in_the_workspace()
    {
        var (runtime, server) = Create(_ => (HttpStatusCode.OK, """{"id":"ses_fsociety","title":"Logout"}"""));

        var id = await runtime.CreateSessionAsync(Workspace, "Logout", new AgentSetup("build", null, null, null), default);

        Assert.Equal("ses_fsociety", id);
        Assert.StartsWith("/session?directory=", server.Requests[0].PathAndQuery, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Agents_are_looked_up_by_name()
    {
        var (runtime, _) = Create(_ => (HttpStatusCode.OK, """[{"name":"build","mode":"primary"},{"name":"plan","mode":"primary"}]"""));

        Assert.True(await runtime.HasAgentAsync("build", Workspace, default));
        Assert.False(await runtime.HasAgentAsync("frontend-dev", Workspace, default));
    }

    [Fact]
    public async Task The_last_reply_is_the_last_assistant_text()
    {
        var (runtime, _) = Create(_ => (HttpStatusCode.OK, """
            [{"info":{"role":"user"},"parts":[{"type":"text","text":"Do it"}]},
             {"info":{"role":"assistant"},"parts":[{"type":"text","text":"Looking around."},{"type":"tool","tool":"read"}]},
             {"info":{"role":"assistant"},"parts":[{"type":"tool","tool":"edit"}]},
             {"info":{"role":"assistant"},"parts":[{"type":"text","text":"  Added the logout button.  "}]}]
            """));

        Assert.Equal("Added the logout button.", await runtime.GetLastReplyAsync("ses_1", Workspace, default));
    }

    [Fact]
    public async Task Server_errors_explain_themselves()
    {
        var (runtime, _) = Create(_ => (HttpStatusCode.BadRequest, """{"error":"agent not found"}"""));

        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            runtime.PromptAsync("ses_1", Workspace, new AgentSetup("build", null, null, null), "Do it", default));

        Assert.Contains("400", error.Message, StringComparison.Ordinal);
        Assert.Contains("agent not found", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, "once")]
    [InlineData(false, "reject")]
    public async Task Permission_replies_are_once_or_reject(bool allow, string reply)
    {
        var (runtime, server) = Create(_ => (HttpStatusCode.OK, "true"));

        await runtime.ReplyToPermissionAsync("per_1", Workspace, allow, default);

        Assert.Contains($"\"reply\":\"{reply}\"", server.Requests[0].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Tools_are_registered_in_the_workspace_with_the_task_header()
    {
        var (runtime, server) = Create(_ => (HttpStatusCode.OK, "{}"));
        var taskId = Guid.NewGuid();

        await runtime.ConnectToolsAsync(Workspace, taskId, default);

        var (method, path, body) = Assert.Single(server.Requests);
        Assert.Equal((HttpMethod.Post, "/mcp?directory=%2Fsrv%2Fworktrees%2Fabc"), (method, path));
        using var json = JsonDocument.Parse(body!);
        var config = json.RootElement.GetProperty("config");
        Assert.Equal("openspace", json.RootElement.GetProperty("name").GetString());
        Assert.Equal(("remote", "http://openspace.test/mcp"), (config.GetProperty("type").GetString(), config.GetProperty("url").GetString()));
        Assert.Equal(taskId.ToString(), config.GetProperty("headers").GetProperty("X-Openspace-Task").GetString());
    }

    [Fact]
    public async Task A_role_becomes_session_rules_and_system_instructions()
    {
        var (runtime, server) = Create(r => (HttpStatusCode.OK, r.RequestUri!.AbsolutePath == "/session" ? """{"id":"ses_1"}""" : ""));
        var setup = new AgentSetup("build", null, "Never change code.", new RolePermissions(PermissionLevel.Ask, PermissionLevel.Deny, PermissionLevel.Allow));

        await runtime.CreateSessionAsync(Workspace, "Audit", setup, default);
        await runtime.PromptAsync("ses_1", Workspace, setup, "Do it", default);

        using var session = JsonDocument.Parse(server.Requests[0].Body!);
        var rules = session.RootElement.GetProperty("permission").EnumerateArray()
            .Select(r => (r.GetProperty("permission").GetString(), r.GetProperty("pattern").GetString(), r.GetProperty("action").GetString()));
        Assert.Equal([("external_directory", "*", "ask"), ("edit", "*", "ask"), ("bash", "*", "deny"), ("webfetch", "*", "allow")], rules);
        using var prompt = JsonDocument.Parse(server.Requests[1].Body!);
        Assert.Equal("Never change code.", prompt.RootElement.GetProperty("system").GetString());
    }

    [Fact]
    public async Task Without_a_role_the_session_keeps_OpenCode_defaults_but_stays_in_its_folder()
    {
        var (runtime, server) = Create(_ => (HttpStatusCode.OK, """{"id":"ses_1"}"""));

        await runtime.CreateSessionAsync(Workspace, "Plain", new AgentSetup("build", null, null, null), default);

        using var body = JsonDocument.Parse(server.Requests[0].Body!);
        var rule = Assert.Single(body.RootElement.GetProperty("permission").EnumerateArray());
        Assert.Equal(("external_directory", "ask"), (rule.GetProperty("permission").GetString(), rule.GetProperty("action").GetString()));
    }

    [Fact]
    public async Task A_draft_runs_in_a_session_that_may_touch_nothing_and_is_deleted_after()
    {
        var (runtime, server) = Create(r => r.RequestUri!.AbsolutePath switch
        {
            "/session" => (HttpStatusCode.OK, """{"id":"ses_draft"}"""),
            "/session/ses_draft/message" => (HttpStatusCode.OK, """{"info":{"role":"assistant"},"parts":[{"type":"step-start"},{"type":"text","text":"- Check every form."}]}"""),
            _ => (HttpStatusCode.OK, "true"),
        });

        var draft = await runtime.DraftAsync("You write instructions.", "Role: tester", default);

        Assert.Equal("- Check every form.", draft);
        Assert.DoesNotContain("\"allow\"", server.Requests[0].Body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"ask\"", server.Requests[0].Body, StringComparison.Ordinal);
        Assert.Contains("\"plan\"", server.Requests[1].Body, StringComparison.Ordinal);
        Assert.Equal(HttpMethod.Delete, server.Requests[^1].Method);
    }

    [Fact]
    public async Task Pending_permissions_are_read_from_the_workspace()
    {
        var (runtime, server) = Create(_ => (HttpStatusCode.OK, """
            [{"id":"per_1","sessionID":"ses_1","permission":"bash","patterns":["ls -la","ls wp-content/themes"],"metadata":{},"always":[]}]
            """));

        var pending = await runtime.PendingPermissionsAsync(Workspace, default);

        Assert.Equal(new PermissionRequest("per_1", "ses_1", "run ls -la, ls wp-content/themes"), Assert.Single(pending));
        Assert.Equal("/permission?directory=%2Fsrv%2Fworktrees%2Fabc", server.Requests[0].PathAndQuery);
    }

    [Fact]
    public async Task A_plan_asks_for_structured_json_in_a_read_only_session_and_returns_it()
    {
        var (runtime, server) = Create(r => r.RequestUri!.AbsolutePath switch
        {
            "/session" => (HttpStatusCode.OK, """{"id":"ses_plan"}"""),
            "/session/ses_plan/message" => (HttpStatusCode.OK, """{"info":{"role":"assistant","structured":{"title":"Contact","tasks":[]}},"parts":[]}"""),
            _ => (HttpStatusCode.OK, "true"),
        });

        var json = await runtime.PlanAsync("/srv/repos/e-corp", "Split it.", "Request: contact page", """{"type":"object"}""", default);

        Assert.Equal("""{"title":"Contact","tasks":[]}""", json);
        Assert.StartsWith("/session?directory=%2Fsrv%2Frepos%2Fe-corp", server.Requests[0].PathAndQuery, StringComparison.Ordinal);
        Assert.Contains("\"deny\"", server.Requests[0].Body, StringComparison.Ordinal);
        using var body = JsonDocument.Parse(server.Requests[1].Body!);
        Assert.Equal("json_schema", body.RootElement.GetProperty("format").GetProperty("type").GetString());
        Assert.Equal(HttpMethod.Delete, server.Requests[^1].Method);
    }

    [Fact]
    public async Task A_plan_without_structured_answer_reports_the_model_error()
    {
        var (runtime, _) = Create(r => r.RequestUri!.AbsolutePath switch
        {
            "/session" => (HttpStatusCode.OK, """{"id":"ses_plan"}"""),
            "/session/ses_plan/message" => (HttpStatusCode.OK, """{"info":{"role":"assistant","error":{"name":"StructuredOutputError","data":{"message":"schema mismatch","retries":2}}},"parts":[]}"""),
            _ => (HttpStatusCode.OK, "true"),
        });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runtime.PlanAsync("/srv/repos/e-corp", "Split it.", "Request", """{"type":"object"}""", default));

        Assert.Contains("schema mismatch", error.Message, StringComparison.Ordinal);
    }
}
